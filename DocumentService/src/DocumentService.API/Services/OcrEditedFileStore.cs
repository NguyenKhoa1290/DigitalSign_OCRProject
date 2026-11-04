using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using Npgsql;
using NpgsqlTypes;

namespace DocumentService.API.Services;

public record OcrEditedFile(Guid Id, string FileName, string MinioPath, string SourcePath,
    Guid EditedById, string EditedByName, string EditorRole, DateTimeOffset EditedAt, int Revision);

public sealed class OcrEditedFileStore(NpgsqlDataSource source, IFileStorageService files)
{
    public const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task InitializeAsync(NpgsqlDataSource source)
    {
        await using var command=source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS "OcrEditedFileCleanup" (
              "ObjectName" text PRIMARY KEY,"DocumentId" uuid NOT NULL,"CreatedAt" timestamptz NOT NULL DEFAULT now());
            CREATE OR REPLACE FUNCTION hau_ocr_edited_file_cleanup() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE old_path text; new_path text;
            BEGIN
              old_path := OLD."OcrDataRaw"->'editedDocument'->>'minioPath';
              IF TG_OP<>'DELETE' AND NEW."OcrDataRaw"->'editedDocument'->>'sourcePath'=NEW."MinioPath" THEN
                new_path := NEW."OcrDataRaw"->'editedDocument'->>'minioPath';
              END IF;
              IF old_path IS NOT NULL AND old_path IS DISTINCT FROM new_path
                 AND old_path ~ '^documents/[0-9a-f-]+[.]docx$' THEN
                INSERT INTO "OcrEditedFileCleanup" ("ObjectName","DocumentId")
                  VALUES (substring(old_path from 11),OLD."Id") ON CONFLICT DO NOTHING;
              END IF;
              RETURN NULL;
            END; $$;
            DROP TRIGGER IF EXISTS hau_ocr_edited_file_cleanup_trigger ON "Documents";
            CREATE TRIGGER hau_ocr_edited_file_cleanup_trigger AFTER UPDATE OF "OcrDataRaw","MinioPath" OR DELETE ON "Documents"
              FOR EACH ROW EXECUTE FUNCTION hau_ocr_edited_file_cleanup();
            """);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<OcrEditedFile> UploadAsync(Guid document,Guid actor,Guid version,Stream content,string fileName,CancellationToken token)
    {
        fileName=Path.GetFileName(fileName.Replace('\\','/')).Trim();
        if(fileName.Length is 0 or >200 || fileName.Any(char.IsControl) || !fileName.EndsWith(".docx",StringComparison.OrdinalIgnoreCase))
            throw new DocumentServiceException("Chỉ tải lên bản Word .docx đã chỉnh sửa, tên tệp tối đa 200 ký tự.");
        await using(var connection=await source.OpenConnectionAsync(token))
        { await ValidateAsync(connection,null,document,actor,version,false,token); }
        ValidateWord(content); content.Position=0;
        var stored=await files.UploadFileAsync(content,"ocr-edited.docx",WordMime,"documents");
        try
        {
            await using var connection=await source.OpenConnectionAsync(token);
            await using var transaction=await connection.BeginTransactionAsync(token);
            var (raw,path,name,role)=await ValidateAsync(connection,transaction,document,actor,version,true,token);
            var previous=raw["editedDocument"]?.Deserialize<OcrEditedFile>(Json);
            var edit=new OcrEditedFile(Guid.NewGuid(),fileName,"documents/"+stored,path,actor,name,role,DateTimeOffset.UtcNow,(previous?.Revision??0)+1);
            raw["editedDocument"]=JsonSerializer.SerializeToNode(edit,Json);
            await using(var update=new NpgsqlCommand("UPDATE \"Documents\" SET \"OcrDataRaw\"=@raw,\"ConcurrencyVersion\"=@version WHERE \"Id\"=@doc",connection,transaction))
            {
                update.Parameters.AddWithValue("raw",NpgsqlDbType.Jsonb,raw.ToJsonString());update.Parameters.AddWithValue("version",Guid.NewGuid());update.Parameters.AddWithValue("doc",document);
                await update.ExecuteNonQueryAsync(token);
            }
            await using(var history=new NpgsqlCommand("""
                INSERT INTO "DocumentProcesses" ("Id","DocId","FromUserId","Action","Comment","Timestamp")
                VALUES (@id,@doc,@actor,'OcrEdited',@comment,now())
                """,connection,transaction))
            {
                history.Parameters.AddWithValue("id",edit.Id);history.Parameters.AddWithValue("doc",document);history.Parameters.AddWithValue("actor",actor);
                history.Parameters.AddWithValue("comment",$"Tải lên bản Word OCR đã chỉnh sửa (lần {edit.Revision}): {fileName}.");await history.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);return edit;
        }
        catch
        {
            // If COMMIT succeeded but its response was lost, cleanup checks the document lock
            // and current reference before deleting; it must never delete a committed edit.
            await QueueCleanupAsync(document,stored);throw;
        }
    }

    private static async Task<(JsonObject Raw,string Path,string Name,string Role)> ValidateAsync(NpgsqlConnection connection,NpgsqlTransaction? transaction,Guid doc,Guid actor,Guid version,bool locked,CancellationToken token)
    {
        if(!await OcrJobStore.IsOperatorAsync(connection,transaction,actor,token))throw new UnauthorizedDocumentAccessException("Chỉ Văn thư hoặc Admin được tải lên bản chỉnh sửa OCR.");
        string path;JsonObject raw;
        await using(var command=new NpgsqlCommand("SELECT \"OcrDataRaw\",\"MinioPath\",\"ConcurrencyVersion\" FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\""+(locked?" FOR UPDATE":""),connection,transaction))
        {
            command.Parameters.AddWithValue("doc",doc);await using var reader=await command.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token))throw new DocumentNotFoundException(doc);
            if(reader.GetGuid(2)!=version)throw new DocumentConflictException("Công văn đã thay đổi. Hãy tải lại trước khi gửi bản chỉnh sửa.");
            raw=reader.IsDBNull(0)?new():JsonNode.Parse(reader.GetString(0)) as JsonObject??new();path=reader.IsDBNull(1)?"":reader.GetString(1);
        }
        if(!await OcrJobStore.IsOperatorForDocumentAsync(connection,transaction,actor,doc,token))
            throw new UnauthorizedDocumentAccessException("Chỉ hiệu chỉnh OCR khi hồ sơ còn Nháp hoặc Trả sửa.");
        if(path.EndsWith(".docx",StringComparison.OrdinalIgnoreCase))
            throw new DocumentServiceException("Word là văn bản hoàn chỉnh, không cần OCR.");
        var provider=(raw["provider"]??raw["Provider"])?.GetValue<string>();var layout=raw["layout"]??raw["Layout"];
        if(provider!="gemini" || layout?["blocks"] is not JsonArray {Count:>0})throw new DocumentServiceException("Cần có kết quả OCR trước khi tải lên bản chỉnh sửa.");
        if(string.IsNullOrWhiteSpace(path))throw new DocumentServiceException("Công văn chưa có tệp gốc để đối chiếu.");
        if(raw["sourcePath"]?.GetValue<string>() is {} sourcePath && sourcePath!=path)throw new DocumentConflictException("Tệp gốc đã thay đổi. Hãy chạy OCR lại trước khi gửi bản chỉnh sửa.");
        await using(var jobs=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM \"OcrJobs\" WHERE \"DocumentId\"=@doc AND \"Status\" IN ('Queued','Running'))",connection,transaction))
        {jobs.Parameters.AddWithValue("doc",doc);if((bool)(await jobs.ExecuteScalarAsync(token))!)throw new DocumentConflictException("OCR đang xử lý. Hãy đợi hoàn tất trước khi tải bản chỉnh sửa.");}
        if(Guid.TryParse(raw["jobId"]?.GetValue<string>(),out var jobId))
        {
            await using var job=new NpgsqlCommand("SELECT \"SourcePath\" FROM \"OcrJobs\" WHERE \"Id\"=@id AND \"DocumentId\"=@doc",connection,transaction);
            job.Parameters.AddWithValue("id",jobId);job.Parameters.AddWithValue("doc",doc);
            if(await job.ExecuteScalarAsync(token) is string jobPath && jobPath!=path)throw new DocumentConflictException("Tệp gốc đã thay đổi. Hãy chạy OCR lại trước khi gửi bản chỉnh sửa.");
        }
        await using var person=new NpgsqlCommand("""
            SELECT COALESCE(NULLIF(u."FullName",''),u."Username"),
             CASE WHEN EXISTS(SELECT 1 FROM "AppUserRoles" ur JOIN "AppRoles" r ON r."Id"=ur."RoleId" WHERE ur."UserId"=u."Id" AND r."RoleName"='Admin') THEN 'Admin' ELSE 'Clerk' END
            FROM "AppUsers" u WHERE u."Id"=@actor
            """,connection,transaction);
        person.Parameters.AddWithValue("actor",actor);await using var user=await person.ExecuteReaderAsync(token);await user.ReadAsync(token);
        return(raw,path,user.GetString(0),user.GetString(1));
    }

    public async Task<(Stream Content,string FileName)> DownloadAsync(Guid doc,Guid editId,CancellationToken token)
    {
        await using var connection=await source.OpenConnectionAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        await using var command=new NpgsqlCommand("SELECT \"OcrDataRaw\",\"MinioPath\" FROM \"Documents\" WHERE \"Id\"=@doc AND NOT \"IsDeleted\" FOR UPDATE",connection,transaction);
        command.Parameters.AddWithValue("doc",doc);OcrEditedFile? edit;
        await using(var reader=await command.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token))throw new DocumentNotFoundException(doc);
            var raw=reader.IsDBNull(0)?null:JsonNode.Parse(reader.GetString(0));edit=raw?["editedDocument"]?.Deserialize<OcrEditedFile>(Json);
            if(edit==null || edit.Id!=editId || edit.SourcePath!=reader.GetString(1))throw new DocumentConflictException("Bản chỉnh sửa đã thay đổi. Hãy tải lại kết quả.");
        }
        var content=await files.DownloadFileAsync(edit.MinioPath[10..],"documents");await transaction.CommitAsync(token);return(content,edit.FileName);
    }

    private async Task QueueCleanupAsync(Guid doc,string stored)
    {
        await using var command=source.CreateCommand("INSERT INTO \"OcrEditedFileCleanup\" (\"ObjectName\",\"DocumentId\") VALUES (@object,@doc) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue("object",stored);command.Parameters.AddWithValue("doc",doc);await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> CleanupOneAsync(CancellationToken token)
    {
        await using var connection=await source.OpenConnectionAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        string name;Guid doc;
        await using(var next=new NpgsqlCommand("SELECT \"ObjectName\",\"DocumentId\" FROM \"OcrEditedFileCleanup\" ORDER BY \"CreatedAt\" LIMIT 1",connection,transaction))
        await using(var reader=await next.ExecuteReaderAsync(token))
        {if(!await reader.ReadAsync(token))return false;name=reader.GetString(0);doc=reader.GetGuid(1);}
        var referenced=false;
        await using(var current=new NpgsqlCommand("SELECT \"OcrDataRaw\"->'editedDocument'->>'minioPath',\"OcrDataRaw\"->'editedDocument'->>'sourcePath',\"MinioPath\" FROM \"Documents\" WHERE \"Id\"=@doc FOR UPDATE",connection,transaction))
        {
            current.Parameters.AddWithValue("doc",doc);await using var reader=await current.ExecuteReaderAsync(token);
            if(await reader.ReadAsync(token))referenced=!reader.IsDBNull(0)&&!reader.IsDBNull(1)&&!reader.IsDBNull(2)&&reader.GetString(0)=="documents/"+name&&reader.GetString(1)==reader.GetString(2);
        }
        // Lock in the same order as upload/purge: document first, cleanup row second.
        await using(var own=new NpgsqlCommand("SELECT \"ObjectName\" FROM \"OcrEditedFileCleanup\" WHERE \"ObjectName\"=@name FOR UPDATE SKIP LOCKED",connection,transaction))
        {own.Parameters.AddWithValue("name",name);if(await own.ExecuteScalarAsync(token) is not string)return false;}
        if(!referenced && !await files.DeleteFileAsync(name,"documents"))return false;
        await using(var remove=new NpgsqlCommand("DELETE FROM \"OcrEditedFileCleanup\" WHERE \"ObjectName\"=@name",connection,transaction))
        {remove.Parameters.AddWithValue("name",name);await remove.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);return true;
    }

    public static void ValidateWord(Stream content)
    {
        try
        {
            using var archive=new ZipArchive(content,ZipArchiveMode.Read,true);
            if(archive.Entries.Count>3000 || archive.Entries.Sum(entry=>entry.Length)>100L*1024*1024 || archive.Entries.Any(e=>e.FullName.EndsWith("vbaProject.bin",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException();
            var document=archive.GetEntry("word/document.xml");if(document==null || archive.GetEntry("[Content_Types].xml")==null || document.Length>20*1024*1024)throw new InvalidDataException();
            using var xml=document.Open();using var reader=XmlReader.Create(xml,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});
            reader.MoveToContent();if(reader.LocalName!="document" || reader.NamespaceURI!="http://schemas.openxmlformats.org/wordprocessingml/2006/main")throw new InvalidDataException();
            while(reader.Read()){}
        }
        catch(Exception ex) when(ex is InvalidDataException or XmlException or IOException)
        {throw new DocumentServiceException("Tệp .docx không hợp lệ, có macro hoặc vượt giới hạn giải nén. Hãy lưu lại bằng Word rồi thử lại.");}
    }
}
