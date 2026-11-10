using ApiGateway.Monitoring;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Tests;

public class NotificationRealtimeTests
{
    [Fact]
    public async Task SignalsAreIsolatedByUserAndCoalesced_AndDisposeCompletesReader()
    {
        var signals = new NotificationSignals(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var first = signals.Subscribe(a); var second = signals.Subscribe(a); var other = signals.Subscribe(b);
        for (var i = 0; i < 10; i++) signals.Publish(a);
        Assert.True(first.Reader.TryRead(out _)); Assert.False(first.Reader.TryRead(out _));
        Assert.True(second.Reader.TryRead(out _)); Assert.False(other.Reader.TryRead(out _));
        signals.Unsubscribe(first.Id); await first.Reader.Completion;
        signals.Publish(); Assert.True(second.Reader.TryRead(out _)); Assert.True(other.Reader.TryRead(out _));
        signals.Unsubscribe(second.Id); signals.Unsubscribe(other.Id);
    }

    [DeletionDatabaseFact]
    public async Task DatabaseTriggersDeliverCommittedInsertReadAndDocumentVisibility_WithAuthoritativeUnreadCount()
    {
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_DELETION_TEST_PG"));
        if (options.Database != "doc_deletion_tests" || options.Host != "127.0.0.1" || options.Port == 5432)
            throw new InvalidOperationException("Only the isolated database is allowed.");
        var schema = "realtime_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(options.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            options.SearchPath = schema;
            await using var source = NpgsqlDataSource.Create(options.ConnectionString);
            await using (var seed = source.CreateCommand("""
                CREATE TABLE "AppUsers" ("Id" uuid PRIMARY KEY,"IsActive" boolean NOT NULL DEFAULT true,"DepartmentId" uuid NULL);
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY,"IsDeleted" boolean NOT NULL DEFAULT false,"Status" text,"ProcessingDepartmentId" uuid,"OcrDataRaw" jsonb,"ConcurrencyVersion" uuid NOT NULL DEFAULT gen_random_uuid());
                ALTER TABLE "Documents" ADD COLUMN "DepartmentId" uuid, ADD COLUMN "CreatedById" uuid, ADD COLUMN "AssignedToId" uuid, ADD COLUMN "IsSchoolWide" boolean NOT NULL DEFAULT true;
                CREATE TABLE "AppRoles" ("Id" uuid PRIMARY KEY,"RoleName" text);
                CREATE TABLE "AppUserRoles" ("UserId" uuid,"RoleId" uuid);
                CREATE TABLE "Signatures" ("DocId" uuid,"SignerId" uuid);
                CREATE TABLE "DocumentProcesses" ("Id" uuid PRIMARY KEY,"DocId" uuid,"FromUserId" uuid,"ToUserId" uuid,"Action" text,"Timestamp" timestamptz);
                """)) await seed.ExecuteNonQueryAsync();
            var store = new EventStore(source); await store.InitializeAsync();
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var doc = Guid.NewGuid(); var id = Guid.NewGuid();
            await using (var seed = source.CreateCommand($"INSERT INTO \"AppUsers\" (\"Id\") VALUES ('{a}'),('{b}'); INSERT INTO \"Documents\" (\"Id\") VALUES ('{doc}');")) await seed.ExecuteNonQueryAsync();
            var signals = new NotificationSignals(); var first = signals.Subscribe(a); var other = signals.Subscribe(b);
            using var listener = new NotificationListener(source, signals, NullLogger<NotificationListener>.Instance);
            await listener.StartAsync(default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await first.Reader.ReadAsync(timeout.Token); await other.Reader.ReadAsync(timeout.Token); // LISTEN is ready.
            try
            {
                var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
                builder.Services.AddSingleton(store); builder.Services.AddSingleton(signals);
                builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, StreamAuth>("Fixture", _ => { });
                builder.Services.AddAuthorization(); builder.Services.AddControllers().AddApplicationPart(typeof(NotificationStreamController).Assembly);
                await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                using var unauthorized = await client.GetAsync("/api/notifications/stream"); Assert.Equal(401, (int)unauthorized.StatusCode);
                client.DefaultRequestHeaders.Add("X-Fixture-User", a.ToString());
                using var response = await client.GetAsync($"/api/notifications/stream?userId={b}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
                Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
                async Task<string> Frame()
                {
                    while (await reader.ReadLineAsync(timeout.Token) is { } line) if (line.StartsWith("data: ")) return line;
                    throw new InvalidOperationException("Stream ended before update");
                }
                Assert.Contains("\"unreadCount\":0", await Frame());
                await using var connection = await source.OpenConnectionAsync();
                await using var tx = await connection.BeginTransactionAsync();
                await using (var insert = new NpgsqlCommand($"INSERT INTO \"UserNotifications\" (\"Id\",\"UserId\",\"EventKey\",\"Kind\",\"Message\",\"TargetUrl\",\"DocumentId\") VALUES ('{id}','{a}','fixture','Assignment','Fixture','/documents/{doc}','{doc}')", connection, tx)) await insert.ExecuteNonQueryAsync();
                Assert.False(first.Reader.TryRead(out _));
                await tx.CommitAsync(); await first.Reader.ReadAsync(timeout.Token);
                Assert.Contains("\"unreadCount\":1", await Frame()); // The open HTTP response receives the update without another GET.
                Assert.False(other.Reader.TryRead(out _));
                Assert.Equal(1, await store.GetUnreadCountAsync(a, default)); Assert.Equal(0, await store.GetUnreadCountAsync(b, default));
                Assert.True(await store.MarkReadAsync(a, id, default)); await first.Reader.ReadAsync(timeout.Token);
                Assert.Contains("\"unreadCount\":0", await Frame());
                Assert.Equal(0, await store.GetUnreadCountAsync(a, default));
                await using (var unread = source.CreateCommand($"UPDATE \"UserNotifications\" SET \"ReadAt\"=NULL WHERE \"Id\"='{id}'")) await unread.ExecuteNonQueryAsync();
                await first.Reader.ReadAsync(timeout.Token);
                await using (var hide = source.CreateCommand($"UPDATE \"Documents\" SET \"IsDeleted\"=true WHERE \"Id\"='{doc}'")) await hide.ExecuteNonQueryAsync();
                await first.Reader.ReadAsync(timeout.Token); Assert.Equal(0, await store.GetUnreadCountAsync(a, default));
                await using (var restore = source.CreateCommand($"UPDATE \"Documents\" SET \"IsDeleted\"=false WHERE \"Id\"='{doc}'")) await restore.ExecuteNonQueryAsync();
                await first.Reader.ReadAsync(timeout.Token); Assert.Equal(1, await store.GetUnreadCountAsync(a, default));
                await using (var purge = source.CreateCommand($"DELETE FROM \"Documents\" WHERE \"Id\"='{doc}'")) await purge.ExecuteNonQueryAsync();
                await first.Reader.ReadAsync(timeout.Token); Assert.Equal(0, await store.GetUnreadCountAsync(a, default));
            }
            finally { await listener.StopAsync(default); signals.Unsubscribe(first.Id); signals.Unsubscribe(other.Id); }
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin); await drop.ExecuteNonQueryAsync(); }
    }

    private sealed class StreamAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Fixture-User"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
