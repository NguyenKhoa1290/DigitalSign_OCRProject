using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Common;
using Npgsql;
using NpgsqlTypes;

namespace DocumentService.API.Services;

public record OcrJobRequest(Guid ExpectedVersion, string ModelId, string Pages);
public record OcrJobDto(Guid Id, Guid DocumentId, string Status, string ModelId, string Pages,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, string? Error);
public record OcrJobRow(OcrJobDto Info, Guid ActorId, string SourcePath, Guid SourceVersion, string DocumentTitle);

public sealed class OcrJobStore(NpgsqlDataSource source)
{
    public const long WorkerLock = 5206538465837435458;
    private const string Columns = "\"Id\",\"DocumentId\",\"Status\",\"ModelId\",\"Pages\",\"CreatedAt\",\"StartedAt\",\"CompletedAt\",\"Error\",\"ActorId\",\"SourcePath\",\"SourceVersion\",\"DocumentTitle\"";
    public Task<NpgsqlConnection> OpenAsync(CancellationToken token) => source.OpenConnectionAsync(token).AsTask();

    public async Task InitializeAsync()
    {
        await using var cmd = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS "OcrJobs" (
              "Id" uuid PRIMARY KEY, "DocumentId" uuid NOT NULL, "ActorId" uuid NOT NULL,
              "SourcePath" text NOT NULL, "SourceVersion" uuid NOT NULL, "DocumentTitle" varchar(500) NOT NULL,
              "ModelId" varchar(100) NOT NULL, "Pages" varchar(200) NOT NULL,
              "Status" varchar(20) NOT NULL CHECK ("Status" IN ('Queued','Running','Succeeded','Failed')),
              "CreatedAt" timestamptz NOT NULL DEFAULT now(), "StartedAt" timestamptz NULL,
              "CompletedAt" timestamptz NULL, "Error" varchar(1000) NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OcrJobs_OneActiveDocument" ON "OcrJobs" ("DocumentId") WHERE "Status" IN ('Queued','Running');
            CREATE INDEX IF NOT EXISTS "IX_OcrJobs_Queue" ON "OcrJobs" ("Status","CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_OcrJobs_Document" ON "OcrJobs" ("DocumentId","CreatedAt" DESC);
            ALTER TABLE "OcrJobs" ADD COLUMN IF NOT EXISTS "PublishedAt" timestamptz NULL;
            CREATE INDEX IF NOT EXISTS "IX_OcrJobs_Outbox" ON "OcrJobs" ("PublishedAt","CreatedAt") WHERE "Status"='Queued';
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<OcrJobDto> EnqueueAsync(Guid documentId, Guid actor, OcrJobRequest request, CancellationToken token)
    {
        var model = (request.ModelId ?? "").Trim();
        if (model.Length is < 5 or > 100 || !Regex.IsMatch(model, @"^gemini-[a-zA-Z0-9._-]+$"))
            throw new DocumentServiceException("Chọn model Gemini hợp lệ.");
        var pages = CanonicalPages(request.Pages);
        await using var connection = await OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await IsOperatorAsync(connection, transaction, actor, token))
            throw new UnauthorizedDocumentAccessException("Bạn không có quyền chạy OCR.");
        string path, title; Guid version;
        await using (var cmd = Command(connection, transaction, "SELECT \"MinioPath\",\"ConcurrencyVersion\",\"Title\" FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\" FOR UPDATE", ("doc",documentId)))
        await using (var reader = await cmd.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new DocumentNotFoundException(documentId);
            path = reader.IsDBNull(0) ? "" : reader.GetString(0); version = reader.GetGuid(1); title = reader.GetString(2);
        }
        if (!await IsOperatorForDocumentAsync(connection, transaction, actor, documentId, token))
            throw new UnauthorizedDocumentAccessException("Văn thư chỉ chạy OCR công văn đến.");
        if (version != request.ExpectedVersion) throw new DocumentConflictException("Công văn đã thay đổi. Hãy tải lại trước khi chạy OCR.");
        if (string.IsNullOrWhiteSpace(path)) throw new DocumentServiceException("Vui lòng tải tệp lên trước khi chạy OCR.");
        if (!Regex.IsMatch(path, @"\.(pdf|png|jpe?g|webp)$", RegexOptions.IgnoreCase)) throw new DocumentServiceException("Chỉ nhận diện tệp PDF hoặc ảnh. Word không cần OCR.");
        await using var existing = Command(connection, transaction, $"SELECT {Columns} FROM \"OcrJobs\" WHERE \"DocumentId\"=@doc AND \"Status\" IN ('Queued','Running')", ("doc",documentId));
        var active = await ReadAsync(existing, token);
        if (active != null) { await transaction.CommitAsync(token); return active.Info; }
        var id = Guid.NewGuid();
        await using var insert = Command(connection, transaction, $"""
            INSERT INTO "OcrJobs" ("Id","DocumentId","ActorId","SourcePath","SourceVersion","DocumentTitle","ModelId","Pages","Status")
            VALUES (@id,@doc,@actor,@path,@version,@title,@model,@pages,'Queued') RETURNING {Columns}
            """, ("id",id),("doc",documentId),("actor",actor),("path",path),("version",version),("title",title),("model",model),("pages",pages));
        var job = (await ReadAsync(insert, token))!;
        await transaction.CommitAsync(token);
        return job.Info;
    }

    public async Task<OcrJobDto?> LatestAsync(Guid documentId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var exists = Command(connection,null,"SELECT EXISTS(SELECT 1 FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\")",("doc",documentId));
        if (!(bool)(await exists.ExecuteScalarAsync(token))!) throw new DocumentNotFoundException(documentId);
        await using var cmd = Command(connection,null,$"SELECT {Columns} FROM \"OcrJobs\" WHERE \"DocumentId\"=@doc ORDER BY \"CreatedAt\" DESC,\"Id\" DESC LIMIT 1",("doc",documentId));
        return (await ReadAsync(cmd,token))?.Info;
    }

    public async Task<bool> PublishNextAsync(IOcrJobQueue queue, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var next = Command(connection, transaction, """
            SELECT "Id" FROM "OcrJobs" WHERE "Status"='Queued'
              AND ("PublishedAt" IS NULL OR "PublishedAt" < now()-interval '5 minutes')
            ORDER BY "CreatedAt","Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """);
        if (await next.ExecuteScalarAsync(token) is not Guid id) return false;
        // Keep the pending command when publication fails. A crash after broker ACK may
        // publish twice; the consumer claims only Queued jobs by ID before any AI call.
        await queue.PublishAsync(id, token);
        await using var mark = Command(connection, transaction,
            "UPDATE \"OcrJobs\" SET \"PublishedAt\"=now() WHERE \"Id\"=@id", ("id",id));
        await mark.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<OcrJobRow?> ClaimAsync(NpgsqlConnection connection, Guid jobId, CancellationToken token)
    {
        await using var cmd = Command(connection,null,$"""
            UPDATE "OcrJobs" SET "Status"='Running',"StartedAt"=now()
            WHERE "Id"=@id AND "Status"='Queued'
            RETURNING {Columns}
            """, ("id",jobId));
        return await ReadAsync(cmd,token);
    }

    public async Task RecoverInterruptedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        var jobs = new List<OcrJobRow>();
        await using (var cmd = Command(connection,null,$"SELECT {Columns} FROM \"OcrJobs\" WHERE \"Status\"='Running'"))
        await using (var reader = await cmd.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) jobs.Add(Row(reader));
        foreach (var job in jobs) await FailAsync(connection,job,"OCR bị gián đoạn khi dịch vụ khởi động lại. Hãy chạy lại nếu cần; hệ thống không tự gọi lại AI.",token);
    }

    public async Task CompleteAsync(NpgsqlConnection connection, OcrJobRow job, string result, CancellationToken token)
    {
        var layout = JsonNode.Parse(result) as JsonObject;
        if (result.Length > 2_000_000 || layout?["provider"]?.GetValue<string>() != "gemini" || layout["layout"]?["blocks"] is not JsonArray { Count: > 0 })
            throw new DocumentServiceException("Dịch vụ OCR không trả về bố cục hợp lệ.");
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await LockRunningAsync(connection,transaction,job.Info.Id,token)) return;
        string title, fileName, path; JsonNode? previousRaw;
        await using (var cmd = Command(connection,transaction,"SELECT \"Title\",\"MinioPath\",\"FileName\",\"OcrDataRaw\" FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\" FOR UPDATE",("doc",job.Info.DocumentId)))
        await using (var reader = await cmd.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new DocumentServiceException("Không lưu được OCR vì công văn đã bị xóa.");
            title=reader.GetString(0);path=reader.IsDBNull(1)?"":reader.GetString(1);fileName=reader.IsDBNull(2)?Path.GetFileName(path):reader.GetString(2);
            previousRaw=reader.IsDBNull(3)?null:JsonNode.Parse(reader.GetString(3));
        }
        if (path != job.SourcePath) throw new DocumentConflictException("Tệp đính kèm đã thay đổi trong lúc OCR. Kết quả cũ không được gắn vào tệp mới.");
        if (!await IsOperatorForDocumentAsync(connection, transaction, job.ActorId, job.Info.DocumentId, token))
            throw new UnauthorizedDocumentAccessException("Tài khoản không còn quyền OCR công văn này.");
        layout!["sourceFileNames"] = new JsonArray(fileName);
        layout["jobId"] = job.Info.Id.ToString();
        layout["sourcePath"] = path;
        OcrEditMetadata.Protect(layout,previousRaw,path); // Preserve the human Word, never trust editor metadata from AI.
        await using (var cmd = Command(connection,transaction,"UPDATE \"Documents\" SET \"OcrDataRaw\"=@result,\"ConcurrencyVersion\"=@version WHERE \"Id\"=@doc",("version",Guid.NewGuid()),("doc",job.Info.DocumentId)))
        {
            cmd.Parameters.AddWithValue("result",NpgsqlDbType.Jsonb,layout.ToJsonString());
            await cmd.ExecuteNonQueryAsync(token);
        }
        await using (var cmd = Command(connection,transaction,"""
            INSERT INTO "DocumentProcesses" ("Id","DocId","FromUserId","Action","Comment","Timestamp")
            VALUES (@id,@doc,@actor,'GeminiOcrCompleted',@comment,now())
            """,("id",job.Info.Id),("doc",job.Info.DocumentId),("actor",job.ActorId),("comment",$"Hoàn tất OCR Gemini {job.Info.ModelId}, trang {job.Info.Pages}.")))
            await cmd.ExecuteNonQueryAsync(token);
        await using (var cmd = Command(connection,transaction,"UPDATE \"OcrJobs\" SET \"Status\"='Succeeded',\"CompletedAt\"=now(),\"Error\"=NULL WHERE \"Id\"=@id",("id",job.Info.Id)))
            await cmd.ExecuteNonQueryAsync(token);
        await NotifyAsync(connection,transaction,job,true,$"OCR hoàn tất công văn “{title[..Math.Min(title.Length,160)]}”.",token);
        await transaction.CommitAsync(token);
    }

    public async Task FailAsync(NpgsqlConnection connection, OcrJobRow job, string error, CancellationToken token)
    {
        error = error[..Math.Min(error.Length,900)];
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var cmd = Command(connection,transaction,"UPDATE \"OcrJobs\" SET \"Status\"='Failed',\"CompletedAt\"=now(),\"Error\"=@error WHERE \"Id\"=@id AND \"Status\"='Running'",("id",job.Info.Id),("error",error));
        if (await cmd.ExecuteNonQueryAsync(token) == 0) return;
        await NotifyAsync(connection,transaction,job,false,$"OCR công văn “{job.DocumentTitle[..Math.Min(job.DocumentTitle.Length,160)]}” không thành công. " + error,token);
        await transaction.CommitAsync(token);
    }

    private static async Task NotifyAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,OcrJobRow job,bool success,string message,CancellationToken token)
    {
        await using var check=Command(connection,transaction,"SELECT EXISTS(SELECT 1 FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\")",("doc",job.Info.DocumentId));
        var active=(bool)(await check.ExecuteScalarAsync(token))!;
        await using var cmd = Command(connection,transaction,"""
            INSERT INTO "UserNotifications" ("UserId","EventKey","Kind","Message","TargetUrl","DocumentId")
            SELECT "Id",@key,@kind,@message,@target,@document FROM "AppUsers" WHERE "Id"=@actor AND "IsActive"
            ON CONFLICT ("UserId","EventKey") DO NOTHING
            """,("actor",job.ActorId),("key","ocr-job:"+job.Info.Id),("kind",success?"OcrCompleted":"OcrFailed"),("message",message),
            ("target",active?$"/documents/{job.Info.DocumentId}#document-ocr-result":"/notifications"));
        cmd.Parameters.AddWithValue("document",NpgsqlDbType.Uuid,success?job.Info.DocumentId:DBNull.Value);
        await cmd.ExecuteNonQueryAsync(token);
    }

    public static async Task<bool> IsOperatorForDocumentAsync(NpgsqlConnection connection,NpgsqlTransaction? transaction,Guid actor,Guid document,CancellationToken token)
    {
        await using var cmd=Command(connection,transaction,"""
            SELECT EXISTS(SELECT 1 FROM "AppUsers" u JOIN "AppUserRoles" ur ON ur."UserId"=u."Id"
            JOIN "AppRoles" r ON r."Id"=ur."RoleId" JOIN "Documents" d ON d."Id"=@doc
            LEFT JOIN "DocumentTypes" dt ON dt."Id"=d."DocTypeId"
            WHERE u."Id"=@actor AND u."IsActive" AND NOT d."IsDeleted"
              AND (r."RoleName"='Admin' OR r."RoleName"='Clerk' AND dt."TypeName"='CongVanDen'))
            """,("actor",actor),("doc",document));
        return (bool)(await cmd.ExecuteScalarAsync(token))!;
    }

    public static async Task<bool> IsOperatorAsync(NpgsqlConnection connection,NpgsqlTransaction? transaction,Guid actor,CancellationToken token)
    {
        await using var cmd=Command(connection,transaction,"""
            SELECT EXISTS(SELECT 1 FROM "AppUsers" u JOIN "AppUserRoles" ur ON ur."UserId"=u."Id"
            JOIN "AppRoles" r ON r."Id"=ur."RoleId" WHERE u."Id"=@actor AND u."IsActive" AND r."RoleName" IN ('Admin','Clerk'))
            """,("actor",actor));
        return (bool)(await cmd.ExecuteScalarAsync(token))!;
    }
    private static async Task<bool> LockRunningAsync(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken token)
    {
        await using var cmd=Command(c,t,"SELECT \"Id\" FROM \"OcrJobs\" WHERE \"Id\"=@id AND \"Status\"='Running' FOR UPDATE",("id",id));
        return await cmd.ExecuteScalarAsync(token) is Guid;
    }
    private static NpgsqlCommand Command(NpgsqlConnection c,NpgsqlTransaction? t,string sql,params (string Name,object Value)[] values)
    {
        var cmd=new NpgsqlCommand(sql,c,t);foreach(var (name,value) in values)cmd.Parameters.AddWithValue(name,value);return cmd;
    }
    private static async Task<OcrJobRow?> ReadAsync(NpgsqlCommand command,CancellationToken token)
    { await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?Row(reader):null; }
    private static OcrJobRow Row(NpgsqlDataReader r)=>new(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDateTime(5),r.IsDBNull(6)?null:r.GetDateTime(6),r.IsDBNull(7)?null:r.GetDateTime(7),r.IsDBNull(8)?null:r.GetString(8)),r.GetGuid(9),r.GetString(10),r.GetGuid(11),r.GetString(12));

    public static string CanonicalPages(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length>200) throw new DocumentServiceException("Chọn tối đa 5 trang OCR.");
        var selected=new SortedSet<int>();
        foreach(var part in expression.Split(','))
        {
            var match=Regex.Match(part,@"^\s*(\d+)(?:\s*-\s*(\d+))?\s*$");
            if(!match.Success || !int.TryParse(match.Groups[1].Value,out var start) || start<1)throw new DocumentServiceException("Lựa chọn trang OCR không hợp lệ.");
            var end=start;
            if(match.Groups[2].Success && !int.TryParse(match.Groups[2].Value,out end) || end<start || (long)end-start>4)throw new DocumentServiceException("Chọn tối đa 5 trang OCR.");
            for(long n=start;n<=end;n++)selected.Add((int)n);
            if(selected.Count>5)throw new DocumentServiceException("Chọn tối đa 5 trang OCR.");
        }
        return string.Join(',',selected);
    }
}
