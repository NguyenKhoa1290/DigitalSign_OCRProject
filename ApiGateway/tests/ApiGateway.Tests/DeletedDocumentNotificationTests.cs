using ApiGateway.Monitoring;
using Npgsql;

namespace ApiGateway.Tests;

public sealed class DeletionDatabaseFactAttribute : FactAttribute
{
    public DeletionDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HAU_DELETION_TEST_PG")))
            Skip = "Requires the isolated doc_deletion_tests database.";
    }
}

public class DeletedDocumentNotificationTests
{
    [DeletionDatabaseFact]
    public async Task DeletedDocumentsAreExcludedFromListAndUnreadCount_AndAuditRowsAreRetained()
    {
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_DELETION_TEST_PG"));
        if (options.Database != "doc_deletion_tests" || options.Host != "127.0.0.1" || options.Port == 5432)
            throw new InvalidOperationException("Only the isolated test database is allowed.");
        var schema = "notifications_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(options.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            options.SearchPath = schema;
            await using var source = NpgsqlDataSource.Create(options.ConnectionString);
            await using (var seed = source.CreateCommand("""
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY,"IsDeleted" boolean NOT NULL);
                CREATE TABLE "UserNotifications" ("Id" uuid PRIMARY KEY,"UserId" uuid NOT NULL,"DocumentId" uuid NULL,
                 "Kind" text NOT NULL,"Message" text NOT NULL,"TargetUrl" text NOT NULL,"Timestamp" timestamptz NOT NULL DEFAULT now(),"ReadAt" timestamptz NULL);
                INSERT INTO "Documents" VALUES ('aaaaaaaa-0000-0000-0000-000000000001',true),('aaaaaaaa-0000-0000-0000-000000000002',false);
                INSERT INTO "UserNotifications" ("Id","UserId","DocumentId","Kind","Message","TargetUrl") VALUES
                 (gen_random_uuid(),'bbbbbbbb-0000-0000-0000-000000000001','aaaaaaaa-0000-0000-0000-000000000001','OcrCompleted','Deleted','/deleted'),
                 (gen_random_uuid(),'bbbbbbbb-0000-0000-0000-000000000001','aaaaaaaa-0000-0000-0000-000000000002','OcrCompleted','Active','/active'),
                 (gen_random_uuid(),'bbbbbbbb-0000-0000-0000-000000000001',NULL,'Certificate','Certificate','/certificate');
                """)) await seed.ExecuteNonQueryAsync();
            var store = new EventStore(source);
            var result = await store.GetNotificationsAsync(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), 1, false, null, default);
            Assert.Equal(2, result.TotalCount); Assert.Equal(2, result.UnreadCount); Assert.Equal(2, result.Items.Count);
            await using var count = source.CreateCommand("SELECT count(*) FROM \"UserNotifications\"");
            Assert.Equal(3L, await count.ExecuteScalarAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
