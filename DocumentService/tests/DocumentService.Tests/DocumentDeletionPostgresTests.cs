using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SignDocumentRepository = SignService.Infrastructure.Repositories.DocumentFileRepository;
using SignContext = SignService.Infrastructure.Data.AppDbContext;

namespace DocumentService.Tests;

public sealed class DeletionPostgresFactAttribute : FactAttribute
{
    public DeletionPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HAU_DELETION_TEST_PG")))
            Skip = "Set HAU_DELETION_TEST_PG to a disposable doc_deletion_tests database on a non-production port.";
    }
}

public class DocumentDeletionPostgresTests
{
    [DeletionPostgresFact]
    public async Task PermanentPurge_CascadesProcessesNotificationsAndSignatures_ButKeepsAudit()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var db = f.Context();
        var doc = await db.Documents.SingleAsync(d => d.Id == f.Id);
        doc.IsDeleted = true;
        doc.ConcurrencyVersion = Guid.Empty;
        db.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = doc.Id, Action = "Cancel", Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"UserNotifications\" (\"DocumentId\" uuid REFERENCES \"Documents\" (\"Id\") ON DELETE CASCADE)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"UserNotifications\" VALUES ({doc.Id})");
        var signature = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Signatures\" VALUES ({signature},{doc.Id},{f.Owner},'LegalSeal',{now})");
        db.ChangeTracker.Clear();
        var files = new Moq.Mock<DocumentService.Core.Interfaces.IFileStorageService>();
        files.Setup(s => s.DeleteFileAsync(Moq.It.IsAny<string>(), "documents")).Returns(Task.FromResult(true));
        var service = new DocumentService.Infrastructure.Services.DocumentPurgeService(db, files.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentService.Infrastructure.Services.DocumentPurgeService>.Instance);
        var request = new DocumentService.Core.DTOs.DocumentPurgeDto { Reason = "Remove fixture", ConfirmationTitle = doc.Title, ExpectedVersion = Guid.Empty };
        (await service.PurgeAsync(doc.Id, new(f.Owner, ["Admin"]), request)).FilesPendingCleanup.Should().BeFalse();
        (await db.Documents.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.DocumentProcesses.CountAsync()).Should().Be(0);
        (await db.SignatureEvidence.CountAsync()).Should().Be(0);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"UserNotifications\"").SingleAsync()).Should().Be(0);
        (await db.DocumentPurgeRecords.SingleAsync()).ActorId.Should().Be(f.Owner);
        (await service.PurgeAsync(doc.Id, new(f.Owner, ["Admin"]), request)).FilesPendingCleanup.Should().BeFalse();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _schema = "deletion_" + Guid.NewGuid().ToString("N");
        private readonly string _admin;
        public readonly Guid Owner = Guid.NewGuid();
        public readonly Guid Id = Guid.NewGuid();
        public string ConnectionString { get; }
        public DocumentActor Actor => new(Owner, ["Specialist"]);
        public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
        public Fixture()
        {
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_DELETION_TEST_PG"));
            if (connection.Database != "doc_deletion_tests" || connection.Host != "127.0.0.1" || connection.Port == 5432)
                throw new InvalidOperationException("Only the isolated test database is allowed.");
            _admin = connection.ConnectionString; connection.SearchPath = _schema; ConnectionString = connection.ConnectionString;
        }
        public async Task Initialize(bool legacy = false)
        {
            await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA {_schema}", admin); await command.ExecuteNonQueryAsync();
            await using var db = Context();
            if (legacy)
            {
                await db.GetService<IMigrator>().MigrateAsync("20261014143315_StoreOriginalPdfFileName");
                var type = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Documents\" (\"Id\",\"Title\",\"MinioPath\",\"Status\",\"DocTypeId\") VALUES ({Id},'Legacy','','Draft',{type})");
            }
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "Signatures" ("Id" uuid PRIMARY KEY, "DocId" uuid NOT NULL, "SignerId" uuid NOT NULL,
                 "SignatureType" varchar(100) NOT NULL, "SignedAt" timestamptz NOT NULL)
                """);
            if (!legacy)
            {
                db.Add(new Document { Id = Id, Title = "PG draft", CreatedById = Owner, CreatedAt = DateTime.UtcNow,
                    DeletionEligibilityConfirmed = true, MinioPath = "documents/fixture.pdf",
                    DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002") });
                await db.SaveChangesAsync();
            }
        }
        public async ValueTask DisposeAsync()
        {
            // Only the generated schema in the strictly checked disposable test DB is removed.
            await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", admin);
            await command.ExecuteNonQueryAsync();
        }
    }

    [DeletionPostgresFact]
    public async Task AdminCanCancelPublishedSignedDocument_AndItDisappearsFromSigningAndStatistics()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var db = f.Context();
        var doc = (await db.Documents.SingleAsync(d => d.Id == f.Id));
        doc.Status = DocumentStatus.Published;
        doc.ConcurrencyVersion = Guid.Empty; // Match the actual legacy migration default.
        await db.SaveChangesAsync(); var version = doc.ConcurrencyVersion; db.ChangeTracker.Clear();
        var signatureId = Guid.NewGuid(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Signatures\" (\"Id\",\"DocId\",\"SignerId\",\"SignatureType\",\"SignedAt\") VALUES ({signatureId},{f.Id},{f.Owner},'LegalSeal',{now})");
        var repo = new DocumentRepository(db);
        await repo.CancelAsync(f.Id, new(f.Owner, ["Admin"]), "Cancel publication", version);
        db.ChangeTracker.Clear();
        var saved = (await repo.GetIncludingDeletedAsync(f.Id))!;
        saved.Status.Should().Be(DocumentStatus.Published); saved.MinioPath.Should().Be("documents/fixture.pdf");
        saved.Processes.Should().ContainSingle(p => p.Action == DocumentAction.Cancel);
        (await db.SignatureEvidence.CountAsync()).Should().Be(1);
        (await repo.GetStatisticsAsync(f.Owner, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1))).TotalDocuments.Should().Be(0);
        await using var signDb = new SignContext(new DbContextOptionsBuilder<SignContext>().UseNpgsql(f.ConnectionString).Options);
        var signing = new SignDocumentRepository(signDb);
        (await signing.GetMinioPathAsync(f.Id)).Should().BeNull();
        await FluentActions.Invoking(() => signing.BeginSigningAsync(f.Id)).Should().ThrowAsync<SignService.Core.Exceptions.DocumentNotFoundException>();
    }

    [DeletionPostgresFact]
    public async Task CancellationWaitsForSigningLock_AndRejectsTheOldVersion()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var db = f.Context(); var repo = new DocumentRepository(db);
        var version = (await repo.GetByIdAsync(f.Id))!.ConcurrencyVersion;
        await using var signDb = new SignContext(new DbContextOptionsBuilder<SignContext>().UseNpgsql(f.ConnectionString).Options);
        await using var lease = await new SignDocumentRepository(signDb).BeginSigningAsync(f.Id);
        var cancellation = repo.CancelAsync(f.Id, new(f.Owner, ["Admin"]), "Cancel", version);
        (await Task.WhenAny(cancellation, Task.Delay(150))).Should().NotBe(cancellation);
        await lease.CompleteAsync();
        await FluentActions.Invoking(() => cancellation).Should().ThrowAsync<DocumentConflictException>();
        db.ChangeTracker.Clear();
        var current = (await repo.GetByIdAsync(f.Id))!;
        current.IsDeleted.Should().BeFalse();
        await repo.CancelAsync(f.Id, new(f.Owner, ["Admin"]), "Reviewed signing result", current.ConcurrencyVersion);
    }

    [DeletionPostgresFact]
    public async Task MigrationPreservesLegacyAndDoesNotAutomaticallyGrantDeletion()
    {
        await using var f = new Fixture(); await f.Initialize(legacy: true);
        await using var db = f.Context(); var repo = new DocumentRepository(db);
        var doc = (await repo.GetByIdAsync(f.Id))!;
        doc.Title.Should().Be("Legacy"); doc.IsDeleted.Should().BeFalse(); doc.DeletionEligibilityConfirmed.Should().BeFalse();
        await FluentActions.Invoking(() => repo.SetDeletedAsync(f.Id, new(f.Owner, ["Admin"]), "Hỗ trợ", true))
            .Should().ThrowAsync<DocumentConflictException>();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [DeletionPostgresFact]
    public async Task SigningLocksTheDocumentAndThePersistedMarkerPreventsDeletionEvenWithoutASignature()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var signingDb = new SignContext(new DbContextOptionsBuilder<SignContext>().UseNpgsql(f.ConnectionString).Options);
        var signing = new SignDocumentRepository(signingDb);
        await using var lease = await signing.BeginSigningAsync(f.Id);
        await using var deletingDb = f.Context();
        var deletion = new DocumentRepository(deletingDb).SetDeletedAsync(f.Id, f.Actor, null, true);
        (await Task.WhenAny(deletion, Task.Delay(150))).Should().NotBe(deletion);
        await lease.CompleteAsync();
        await FluentActions.Invoking(() => deletion).Should().ThrowAsync<DocumentConflictException>();
        (await new DocumentRepository(deletingDb).GetByIdAsync(f.Id))!.SigningStartedAt.Should().NotBeNull();
        (await signing.GetMinioPathAsync(f.Id)).Should().Be("documents/fixture.pdf");
    }

    [DeletionPostgresFact]
    public async Task DeletedDocumentIsInvisibleToSignServiceAndCannotAcquireASigningLease()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var db = f.Context(); await new DocumentRepository(db).SetDeletedAsync(f.Id, f.Actor, null, true);
        await using var signingDb = new SignContext(new DbContextOptionsBuilder<SignContext>().UseNpgsql(f.ConnectionString).Options);
        var signing = new SignDocumentRepository(signingDb);
        (await signing.GetMinioPathAsync(f.Id)).Should().BeNull();
        await FluentActions.Invoking(() => signing.BeginSigningAsync(f.Id))
            .Should().ThrowAsync<SignService.Core.Exceptions.DocumentNotFoundException>();
    }

    [DeletionPostgresFact]
    public async Task ConcurrentSubmitAndDeleteCannotBothSucceed()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var submittingDb = f.Context(); await using var deletingDb = f.Context();
        var submitting = new DocumentRepository(submittingDb); var deleting = new DocumentRepository(deletingDb);
        var stale = (await submitting.GetByIdAsync(f.Id))!; stale.Status = DocumentStatus.PendingDeptReview;
        async Task<Exception?> Capture(Func<Task> action) { try { await action(); return null; } catch (Exception ex) { return ex; } }
        var outcomes = await Task.WhenAll(Capture(() => submitting.UpdateAsync(stale)), Capture(() => deleting.SetDeletedAsync(f.Id, f.Actor, null, true)));
        outcomes.Count(x => x == null).Should().Be(1);
        (outcomes.Single(x => x != null) is DocumentConflictException or DbUpdateConcurrencyException).Should().BeTrue();
        await using var check = f.Context(); var saved = (await new DocumentRepository(check).GetIncludingDeletedAsync(f.Id))!;
        (saved.IsDeleted && saved.Status != DocumentStatus.Draft).Should().BeFalse();
    }

    [DeletionPostgresFact]
    public async Task AssignmentChangesVersionBeforeAuditCommitAndBlocksDeletion()
    {
        await using var f = new Fixture(); await f.Initialize();
        await using var db = f.Context(); var repo = new DocumentRepository(db); var stale = (await repo.GetByIdAsync(f.Id))!;
        await using var assignedDb = f.Context();
        await new DocumentProcessRepository(assignedDb).CreateAsync(new() { DocId = f.Id, FromUserId = f.Owner, ToUserId = Guid.NewGuid(), Action = DocumentAction.Assign });
        await FluentActions.Invoking(() => repo.UpdateAsync(stale)).Should().ThrowAsync<DbUpdateConcurrencyException>(); db.ChangeTracker.Clear();
        await FluentActions.Invoking(() => repo.SetDeletedAsync(f.Id, f.Actor, null, true)).Should().ThrowAsync<DocumentConflictException>();
    }
}
