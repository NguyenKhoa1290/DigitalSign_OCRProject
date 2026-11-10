using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApiGateway.Monitoring;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ApiGateway.Tests;

public class DocumentResultRealtimeTests
{
    [ViewerDatabaseFact]
    public async Task DocumentScopeFiltersNotificationsStreamsAndManagerRecipients()
    {
        var options=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES"));
        if(options.Database!="fixture" || options.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Only the isolated fixture database is allowed.");
        await using var admin=new NpgsqlConnection(options.ConnectionString);await admin.OpenAsync();
        var schema="scope_"+Guid.NewGuid().ToString("N");
        await using(var cmd=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await cmd.ExecuteNonQueryAsync();
        try{
            options.SearchPath=schema;await using var source=NpgsqlDataSource.Create(options.ConnectionString);
            async Task Sql(string sql){await using var cmd=source.CreateCommand(sql);await cmd.ExecuteNonQueryAsync();}
            await Sql("""
                CREATE TABLE "AppUsers" ("Id" uuid PRIMARY KEY,"IsActive" boolean NOT NULL DEFAULT true,"DepartmentId" uuid);
                CREATE TABLE "AppRoles" ("Id" uuid PRIMARY KEY,"RoleName" text);
                CREATE TABLE "AppUserRoles" ("UserId" uuid,"RoleId" uuid);
                CREATE TABLE "Signatures" ("DocId" uuid,"SignerId" uuid);
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY,"IsDeleted" boolean NOT NULL DEFAULT false,"OcrDataRaw" jsonb,
                  "ConcurrencyVersion" uuid NOT NULL DEFAULT gen_random_uuid(),"Status" text,"DepartmentId" uuid,"ProcessingDepartmentId" uuid,
                  "CreatedById" uuid,"AssignedToId" uuid,"IsSchoolWide" boolean NOT NULL DEFAULT false);
                CREATE TABLE "DocumentProcesses" ("Id" uuid PRIMARY KEY,"DocId" uuid,"FromUserId" uuid,"ToUserId" uuid,"Action" text,"Timestamp" timestamptz);
                """);
            var store=new EventStore(source);await store.InitializeAsync();
            var deptA=Guid.NewGuid();var deptB=Guid.NewGuid();var author=Guid.NewGuid();var reader=Guid.NewGuid();var other=Guid.NewGuid();var board=Guid.NewGuid();
            var managerRole=Guid.NewGuid();var boardRole=Guid.NewGuid();var doc=Guid.NewGuid();var common=Guid.NewGuid();
            await Sql($"INSERT INTO \"AppUsers\" VALUES ('{author}',true,'{deptA}'),('{reader}',true,'{deptA}'),('{other}',true,'{deptB}'),('{board}',true,'{deptB}'); " +
                $"INSERT INTO \"AppRoles\" VALUES ('{managerRole}','Manager'),('{boardRole}','BoardOfDirectors'); INSERT INTO \"AppUserRoles\" VALUES ('{reader}','{managerRole}'),('{other}','{managerRole}'),('{board}','{boardRole}'); " +
                $"INSERT INTO \"Documents\" (\"Id\",\"Status\",\"DepartmentId\",\"CreatedById\") VALUES ('{doc}','PendingDeptReview','{deptA}','{author}'); " +
                $"INSERT INTO \"Documents\" (\"Id\",\"Status\",\"IsSchoolWide\") VALUES ('{common}','Draft',true); " +
                $"INSERT INTO \"DocumentProcesses\" VALUES ('{Guid.NewGuid()}','{doc}','{author}',NULL,'Submit',now());");
            Assert.NotNull(await store.GetDocumentVersionAsync(doc,reader,default));
            Assert.Null(await store.GetDocumentVersionAsync(doc,other,default));
            Assert.NotNull(await store.GetDocumentVersionAsync(common,other,default));
            Assert.NotNull(await store.GetDocumentVersionAsync(doc,board,default));
            Assert.Equal(1,await store.GetUnreadCountAsync(reader,default));Assert.Equal(0,await store.GetUnreadCountAsync(other,default));
            await Sql($"UPDATE \"AppUsers\" SET \"DepartmentId\"='{deptB}' WHERE \"Id\"='{reader}'");
            Assert.Null(await store.GetDocumentVersionAsync(doc,reader,default));Assert.Equal(0,await store.GetUnreadCountAsync(reader,default));
            Assert.Empty((await store.GetNotificationsAsync(reader,1,false,null,default)).Items);
            await Sql($"INSERT INTO \"Signatures\" VALUES ('{doc}','{reader}')");
            Assert.NotNull(await store.GetDocumentVersionAsync(doc,reader,default));Assert.Equal(1,await store.GetUnreadCountAsync(reader,default));
        }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
    [Fact]
    public async Task DocumentSignalsReachDifferentUsersWatchingSameDocumentOnly()
    {
        var signals=new NotificationSignals();var doc=Guid.NewGuid();var user=Guid.NewGuid();
        var first=signals.Subscribe(user,doc);var leader=signals.Subscribe(Guid.NewGuid(),doc);
        var otherDoc=signals.Subscribe(user,Guid.NewGuid());var global=signals.Subscribe(user);
        for(var i=0;i<10;i++)signals.PublishDocument(doc);
        Assert.True(first.Reader.TryRead(out _));Assert.False(first.Reader.TryRead(out _));
        Assert.True(leader.Reader.TryRead(out _));Assert.False(otherDoc.Reader.TryRead(out _));Assert.False(global.Reader.TryRead(out _));
        signals.Publish(user);
        Assert.True(first.Reader.TryRead(out _));Assert.True(otherDoc.Reader.TryRead(out _));Assert.True(global.Reader.TryRead(out _));
        Assert.False(leader.Reader.TryRead(out _));
        foreach(var subscription in new[]{first,leader,otherDoc,global})signals.Unsubscribe(subscription.Id);
        await first.Reader.Completion;
    }

    [ViewerDatabaseFact]
    public async Task CommittedOcrUpdatesAllReaderRolesWithoutCreatingUnreadNoticesOrLeakingOtherDocuments()
    {
        var configured=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES"));
        if(configured.Database!="fixture" || configured.Host is not ("127.0.0.1" or "localhost"))throw new InvalidOperationException("Only the isolated fixture database is allowed.");
        await using var admin=new NpgsqlConnection(configured.ConnectionString);await admin.OpenAsync();
        var schema="viewers_"+Guid.NewGuid().ToString("N");
        await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
        try
        {
            configured.SearchPath=schema;await using var source=NpgsqlDataSource.Create(configured.ConnectionString);
            async Task Sql(string text){await using var command=source.CreateCommand(text);await command.ExecuteNonQueryAsync();}
            await Sql("""
                CREATE TABLE "AppUsers" ("Id" uuid PRIMARY KEY,"IsActive" boolean NOT NULL DEFAULT true,"DepartmentId" uuid);
                CREATE TABLE "AppRoles" ("Id" uuid PRIMARY KEY,"RoleName" text);
                CREATE TABLE "AppUserRoles" ("UserId" uuid,"RoleId" uuid);
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY,"IsDeleted" boolean NOT NULL DEFAULT false,"OcrDataRaw" jsonb,
                  "ConcurrencyVersion" uuid NOT NULL DEFAULT gen_random_uuid(),"Status" text DEFAULT 'Draft',"ProcessingDepartmentId" uuid,
                  "DepartmentId" uuid,"CreatedById" uuid,"AssignedToId" uuid,"IsSchoolWide" boolean NOT NULL DEFAULT true);
                CREATE TABLE "Signatures" ("DocId" uuid,"SignerId" uuid);
                CREATE TABLE "DocumentProcesses" ("Id" uuid PRIMARY KEY,"DocId" uuid,"FromUserId" uuid,"ToUserId" uuid,"Action" text,"Timestamp" timestamptz);
                """);
            var store=new EventStore(source);await store.InitializeAsync();
            var doc=Guid.NewGuid();var otherDoc=Guid.NewGuid();var writer=Guid.NewGuid();var inactive=Guid.NewGuid();
            var viewers=new[]{(Id:Guid.NewGuid(),Role:"Specialist"),(Id:Guid.NewGuid(),Role:"Manager"),(Id:Guid.NewGuid(),Role:"BoardOfDirectors")};
            await Sql($"INSERT INTO \"AppUsers\" (\"Id\") VALUES ('{writer}'),('{viewers[0].Id}'),('{viewers[1].Id}'),('{viewers[2].Id}'); INSERT INTO \"AppUsers\" VALUES ('{inactive}',false,NULL); INSERT INTO \"Documents\" (\"Id\") VALUES ('{doc}'),('{otherDoc}');");
            var signals=new NotificationSignals();var watched=signals.Subscribe(viewers[0].Id,doc);var unrelated=signals.Subscribe(viewers[0].Id,otherDoc);var noWatch=signals.Subscribe(viewers[0].Id);
            using var listener=new NotificationListener(source,signals,NullLogger<NotificationListener>.Instance);
            await listener.StartAsync(default);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await watched.Reader.ReadAsync(timeout.Token);await unrelated.Reader.ReadAsync(timeout.Token);await noWatch.Reader.ReadAsync(timeout.Token);
            var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
            builder.Services.AddSingleton(store);builder.Services.AddSingleton(signals);
            builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,ViewerAuth>("Fixture",_=>{});
            builder.Services.AddAuthorization();builder.Services.AddControllers().AddApplicationPart(typeof(NotificationStreamController).Assembly);
            await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var clients=new List<HttpClient>();var streams=new List<(HttpResponseMessage Response,StreamReader Reader)>();
            try
            {
                using var anonymous=new HttpClient{BaseAddress=new Uri(address)};
                using(var denied=await anonymous.GetAsync($"/api/notifications/stream?documentId={doc}",timeout.Token))Assert.Equal(401,(int)denied.StatusCode);
                foreach(var viewer in viewers)
                {
                    var client=new HttpClient{BaseAddress=new Uri(address)};clients.Add(client);
                    client.DefaultRequestHeaders.Add("X-Fixture-User",viewer.Id.ToString());client.DefaultRequestHeaders.Add("X-Fixture-Role",viewer.Role);
                    var response=await client.GetAsync($"/api/notifications/stream?documentId={doc}&userId={writer}",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                    Assert.Equal(200,(int)response.StatusCode);Assert.Equal("text/event-stream",response.Content.Headers.ContentType!.MediaType);
                    var reader=new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));streams.Add((response,reader));
                    var initial=await Frame(reader,timeout.Token);Assert.Equal(doc.ToString(),initial.GetProperty("documentId").GetString());Assert.Equal(0,initial.GetProperty("unreadCount").GetInt64());
                }
                using(var missing=await clients[0].GetAsync($"/api/notifications/stream?documentId={Guid.NewGuid()}",timeout.Token))Assert.Equal(404,(int)missing.StatusCode);
                using var inactiveClient=new HttpClient{BaseAddress=new Uri(address)};inactiveClient.DefaultRequestHeaders.Add("X-Fixture-User",inactive.ToString());
                using(var denied=await inactiveClient.GetAsync($"/api/notifications/stream?documentId={doc}",timeout.Token))Assert.Equal(404,(int)denied.StatusCode);

                await using var connection=await source.OpenConnectionAsync();var version=Guid.NewGuid();
                await using(var tx=await connection.BeginTransactionAsync())
                {
                    await using var update=new NpgsqlCommand($"UPDATE \"Documents\" SET \"OcrDataRaw\"='{{\"text\":\"rolled back\"}}',\"ConcurrencyVersion\"='{Guid.NewGuid()}' WHERE \"Id\"='{doc}'",connection,tx);
                    await update.ExecuteNonQueryAsync();await tx.RollbackAsync();Assert.False(watched.Reader.TryRead(out _));
                }
                await using(var tx=await connection.BeginTransactionAsync())
                {
                    await using var update=new NpgsqlCommand($"UPDATE \"Documents\" SET \"OcrDataRaw\"='{{\"text\":\"PRIVATE OCR BODY\"}}',\"ConcurrencyVersion\"='{version}' WHERE \"Id\"='{doc}'; INSERT INTO \"UserNotifications\" (\"UserId\",\"EventKey\",\"Kind\",\"Message\",\"TargetUrl\",\"DocumentId\") VALUES ('{writer}','fixture-ocr','OcrCompleted','Finished','/documents/{doc}','{doc}')",connection,tx);
                    await update.ExecuteNonQueryAsync();Assert.False(watched.Reader.TryRead(out _));await tx.CommitAsync();
                }
                await watched.Reader.ReadAsync(timeout.Token);Assert.False(unrelated.Reader.TryRead(out _));Assert.False(noWatch.Reader.TryRead(out _));
                foreach(var (_,reader) in streams)
                {
                    var frame=await Frame(reader,timeout.Token);Assert.Equal(version.ToString(),frame.GetProperty("documentVersion").GetString());
                    Assert.Equal(0,frame.GetProperty("unreadCount").GetInt64());Assert.DoesNotContain("PRIVATE OCR BODY",frame.GetRawText());
                }
                Assert.Equal(1,await store.GetUnreadCountAsync(writer,default));
                foreach(var viewer in viewers)Assert.Equal(0,await store.GetUnreadCountAsync(viewer.Id,default));
                // A reconnect reconciles changes without depending on missed NOTIFY messages.
                using(var reconnect=await clients[0].GetAsync($"/api/notifications/stream?documentId={doc}",HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                using(var reader=new StreamReader(await reconnect.Content.ReadAsStreamAsync(timeout.Token)))
                    Assert.Equal(version.ToString(),(await Frame(reader,timeout.Token)).GetProperty("documentVersion").GetString());
                await Sql($"UPDATE \"Documents\" SET \"IsDeleted\"=true WHERE \"Id\"='{doc}'");
                foreach(var (_,reader) in streams)Assert.Equal(JsonValueKind.Null,(await Frame(reader,timeout.Token)).GetProperty("documentVersion").ValueKind);
                Assert.Null(await store.GetDocumentVersionAsync(doc,viewers[0].Id,default));
                using(var deleted=await clients[0].GetAsync($"/api/notifications/stream?documentId={doc}",timeout.Token))Assert.Equal(404,(int)deleted.StatusCode);
            }
            finally
            {
                foreach(var (response,reader) in streams){reader.Dispose();response.Dispose();}
                foreach(var client in clients)client.Dispose();
                await listener.StopAsync(default);foreach(var subscription in new[]{watched,unrelated,noWatch})signals.Unsubscribe(subscription.Id);
            }
        }
        finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }

    private static async Task<JsonElement> Frame(StreamReader reader,CancellationToken token)
    {
        while(await reader.ReadLineAsync(token) is {} line)if(line.StartsWith("data: "))return JsonDocument.Parse(line[6..]).RootElement.Clone();
        throw new InvalidOperationException("Stream ended before document update");
    }
    private sealed class ViewerAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Guid.TryParse(Request.Headers["X-Fixture-User"],out var id))return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Role,Request.Headers["X-Fixture-Role"].ToString())},Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
}
public sealed class ViewerDatabaseFactAttribute:FactAttribute
{
    public ViewerDatabaseFactAttribute()
    {if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES")))Skip="Set HAU_OCR_JOB_TEST_POSTGRES to an isolated fixture database.";}
}
