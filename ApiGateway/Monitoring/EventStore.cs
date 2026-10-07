using System.Reflection;
using Npgsql;
using NpgsqlTypes;

namespace ApiGateway.Monitoring;

public class EventStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApiGateway.Monitoring.schema.sql")!;
        var sql = await new StreamReader(resource).ReadToEndAsync();
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = 30;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordAsync(string kind, string service, string action, Guid? actor, Guid? resource,
        string method, string path, int status, string trace, double elapsed)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO "MonitoringEvents" ("Kind","Service","Level","Action","ActorId","ResourceId","Method","Path","StatusCode","TraceId","ElapsedMs")
            VALUES (@kind,@service,@level,@action,@actor,@resource,@method,@path,@status,@trace,@elapsed)
            """);
        cmd.CommandTimeout = 3;
        Add(cmd,"kind",kind); Add(cmd,"service",service); Add(cmd,"level",status >= 500 ? "Error" : status >= 400 ? "Warning" : "Information");
        Add(cmd,"action",action); Add(cmd,"actor",actor,NpgsqlDbType.Uuid); Add(cmd,"resource",resource,NpgsqlDbType.Uuid);
        Add(cmd,"method",method); Add(cmd,"path",path); Add(cmd,"status",status); Add(cmd,"trace",trace); Add(cmd,"elapsed",elapsed);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Guid?> FindLoginActorAsync(string username)
    {
        await using var cmd = dataSource.CreateCommand("SELECT \"Id\" FROM \"AppUsers\" WHERE \"Username\"=@username");
        Add(cmd,"username",username);
        return await cmd.ExecuteScalarAsync() is Guid id ? id : null;
    }

    public async Task<PageResult<MonitoringEvent>> GetEventsAsync(string kind, EventQuery query, CancellationToken token)
    {
        var page = Math.Clamp(query.Page,1,100000); var size = Math.Clamp(query.PageSize,1,100);
        const string where = """
            WHERE e."Kind"=@kind AND (@service IS NULL OR e."Service"=@service) AND (@level IS NULL OR e."Level"=@level)
            AND (@from IS NULL OR e."Timestamp">=@from) AND (@to IS NULL OR e."Timestamp"<@to)
            AND (@trace IS NULL OR e."TraceId"=@trace) AND (@actor IS NULL OR e."ActorId"=@actor)
            """;
        await using var cmd = dataSource.CreateCommand($"""
            SELECT count(*) FROM "MonitoringEvents" e {where};
            SELECT e."Id",e."Service",e."Level",e."Action",e."ActorId",u."FullName",e."ResourceId",e."Timestamp",
                e."Method",e."Path",e."StatusCode",e."TraceId",e."ElapsedMs"
            FROM "MonitoringEvents" e LEFT JOIN "AppUsers" u ON u."Id"=e."ActorId" {where}
            ORDER BY e."Timestamp" DESC,e."Id" DESC LIMIT @size OFFSET @offset;
            """);
        Add(cmd,"kind",kind); Add(cmd,"service",NullIfEmpty(query.Service),NpgsqlDbType.Text); Add(cmd,"level",NullIfEmpty(query.Level),NpgsqlDbType.Text);
        Add(cmd,"from",query.From?.ToUniversalTime(),NpgsqlDbType.TimestampTz); Add(cmd,"to",query.To?.ToUniversalTime(),NpgsqlDbType.TimestampTz);
        Add(cmd,"trace",NullIfEmpty(query.TraceId),NpgsqlDbType.Text); Add(cmd,"actor",query.ActorId,NpgsqlDbType.Uuid); Add(cmd,"size",size); Add(cmd,"offset",(page-1)*size);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        await reader.ReadAsync(token); var count=reader.GetInt64(0); await reader.NextResultAsync(token);
        var rows = new List<MonitoringEvent>();
        while(await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            reader.IsDBNull(4)?null:reader.GetGuid(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetGuid(6),
            reader.GetDateTime(7),reader.GetString(8),reader.GetString(9),reader.GetInt32(10),reader.GetString(11),reader.GetDouble(12)));
        return new(rows,count,page,size);
    }

    public async Task<PageResult<UserNotification>> GetNotificationsAsync(Guid user, int page, bool unreadOnly, string? kind, CancellationToken token)
    {
        page=Math.Clamp(page,1,100000); const int size=20;
        const string where="WHERE \"UserId\"=@user AND (NOT @unread OR \"ReadAt\" IS NULL) AND (@kind IS NULL OR \"Kind\"=@kind)";
        await using var cmd = dataSource.CreateCommand($"""
            SELECT count(*) FROM "UserNotifications" {where};
            SELECT count(*) FROM "UserNotifications" WHERE "UserId"=@user AND "ReadAt" IS NULL;
            SELECT "Id","Kind","Message","TargetUrl","Timestamp","ReadAt" FROM "UserNotifications" {where}
            ORDER BY "Timestamp" DESC,"Id" DESC LIMIT @size OFFSET @offset;
            """);
        Add(cmd,"user",user); Add(cmd,"unread",unreadOnly); Add(cmd,"kind",NullIfEmpty(kind),NpgsqlDbType.Text); Add(cmd,"size",size); Add(cmd,"offset",(page-1)*size);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        await reader.ReadAsync(token); var count=reader.GetInt64(0); await reader.NextResultAsync(token);
        await reader.ReadAsync(token); var unread=reader.GetInt64(0); await reader.NextResultAsync(token);
        var rows=new List<UserNotification>();
        while(await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDateTime(4),reader.IsDBNull(5)?null:reader.GetDateTime(5)));
        return new(rows,count,page,size,unread);
    }

    public async Task<bool> MarkReadAsync(Guid user, Guid? id, CancellationToken token)
    {
        await using var cmd = dataSource.CreateCommand("""
            UPDATE "UserNotifications" SET "ReadAt"=coalesce("ReadAt",now()) WHERE "UserId"=@user AND (@id IS NULL OR "Id"=@id)
            """);
        Add(cmd,"user",user); Add(cmd,"id",id,NpgsqlDbType.Uuid);
        return await cmd.ExecuteNonQueryAsync(token)>0 || id==null;
    }

    public async Task AddCertificateExpiryAsync(Guid user,string thumbprint,string targetUrl,CancellationToken token)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO "UserNotifications" ("UserId","EventKey","Kind","Message","TargetUrl")
            SELECT "Id",@key,'CertificateExpiring','Chứng thư số của bạn sẽ hết hạn trong vòng 30 ngày.',@target
            FROM "AppUsers" WHERE "Id"=@user AND "IsActive"
            ON CONFLICT ("UserId","EventKey") DO NOTHING
            """);
        Add(cmd,"user",user); Add(cmd,"key","certificate:"+thumbprint+":expiry"); Add(cmd,"target",targetUrl); await cmd.ExecuteNonQueryAsync(token);
    }
    private static string? NullIfEmpty(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static void Add(NpgsqlCommand cmd,string name,object? value,NpgsqlDbType? type=null)
    {
        if(type.HasValue) cmd.Parameters.AddWithValue(name,type.Value,value??DBNull.Value);
        else cmd.Parameters.AddWithValue(name,value!);
    }
}
