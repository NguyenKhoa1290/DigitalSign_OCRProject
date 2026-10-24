using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public class DocumentCancellationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db = null!;
        public DocumentRepository Repository => new(Db);
        public readonly Guid AdminId = Guid.NewGuid();
        public DocumentActor Admin => new(AdminId, new[] { "Admin" });
        public async Task<Document> Seed(string status = "Published", bool legacyVersion = false)
        {
            await Connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            await Db.Database.EnsureCreatedAsync();
            await Db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT NOT NULL)");
            var doc = new Document { Id = Guid.NewGuid(), Title = "Cancellation fixture", Status = status,
                DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), MinioPath = "documents/original.pdf",
                FileName = "Original.pdf", ConcurrencyVersion = legacyVersion ? Guid.Empty : Guid.NewGuid() };
            Db.Add(doc);
            Db.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = doc.Id, FromUserId = Guid.NewGuid(), Action = "Create", Timestamp = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            await Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures (DocId) VALUES ({doc.Id})");
            Db.ChangeTracker.Clear();
            return doc;
        }
        public async ValueTask DisposeAsync() { if (Db != null) await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Rejected")]
    [InlineData("PendingDeptReview")]
    [InlineData("DeptSigned")]
    [InlineData("PendingDirectorSign")]
    [InlineData("DirectorSigned")]
    [InlineData("Published")]
    [InlineData("PendingDirection")]
    [InlineData("PendingAssignment")]
    [InlineData("IncomingAssigned")]
    public async Task AdminCancelsAnyStatus_IncludingLegacySignedDocuments_AndPreservesEvidence(string status)
    {
        await using var f = new Fixture(); var doc = await f.Seed(status);
        await f.Repository.CancelAsync(doc.Id, f.Admin, "  Wrong document  ", doc.ConcurrencyVersion);
        f.Db.ChangeTracker.Clear();
        Assert.Null(await f.Repository.GetByIdAsync(doc.Id));
        var saved = (await f.Repository.GetIncludingDeletedAsync(doc.Id))!;
        Assert.True(saved.IsDeleted); Assert.Equal(status, saved.Status);
        Assert.Equal(doc.MinioPath, saved.MinioPath); Assert.Equal(doc.FileName, saved.FileName);
        Assert.Equal(f.AdminId, saved.DeletedById); Assert.Equal("Wrong document", saved.DeleteReason);
        Assert.NotNull(saved.DeletedAt); Assert.NotEqual(doc.ConcurrencyVersion, saved.ConcurrencyVersion);
        Assert.Equal(2, saved.Processes.Count); Assert.Equal(f.AdminId, saved.Processes.Single(p => p.Action == "Cancel").FromUserId);
        Assert.Equal(1, await f.Db.SignatureEvidence.CountAsync());
        Assert.False(DocumentDeletionPolicy.IsEligible(saved));
        await Assert.ThrowsAsync<DocumentConflictException>(() => f.Repository.SetDeletedAsync(doc.Id, f.Admin, "Restore", false));
        f.Db.ChangeTracker.Clear();
        await f.Repository.CancelAsync(doc.Id, f.Admin, "Retry", doc.ConcurrencyVersion);
        Assert.Single((await f.Repository.GetIncludingDeletedAsync(doc.Id))!.Processes, p => p.Action == "Cancel");
    }

    [Fact]
    public async Task ExplicitZeroVersionCancelsLegacyDocument_WithoutChangingOtherDraftRules()
    {
        await using var f = new Fixture(); var doc = await f.Seed(legacyVersion: true);
        Assert.Equal(Guid.Empty, (await f.Repository.GetByIdAsync(doc.Id))!.ConcurrencyVersion);
        Assert.False(DocumentDeletionPolicy.IsEligible(doc));
        await f.Repository.CancelAsync(doc.Id, f.Admin, "Legacy cancellation", Guid.Empty);
        f.Db.ChangeTracker.Clear();
        var saved = (await f.Repository.GetIncludingDeletedAsync(doc.Id))!;
        Assert.True(saved.IsDeleted); Assert.NotEqual(Guid.Empty, saved.ConcurrencyVersion);
        Assert.Equal(doc.MinioPath, saved.MinioPath); Assert.Equal(1, await f.Db.SignatureEvidence.CountAsync());
    }

    [Theory]
    [InlineData("Clerk")]
    [InlineData("Specialist")]
    [InlineData("Manager")]
    [InlineData("BoardOfDirectors")]
    public async Task OnlyAdminCanCancel(string role)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(() => f.Repository.CancelAsync(doc.Id, new(Guid.NewGuid(), new[] { role }), "Reason", doc.ConcurrencyVersion));
        Assert.False((await f.Repository.GetIncludingDeletedAsync(doc.Id))!.IsDeleted);
    }

    [Fact]
    public async Task InvalidReasonStaleVersionAndAuditFailureDoNotCancel()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        foreach (var reason in new[] { " ", new string('x', 1001) })
            await Assert.ThrowsAsync<DocumentServiceException>(() => f.Repository.CancelAsync(doc.Id, f.Admin, reason, doc.ConcurrencyVersion));
        foreach (var version in new[] { Guid.Empty, Guid.NewGuid() })
            await Assert.ThrowsAsync<DocumentConflictException>(() => f.Repository.CancelAsync(doc.Id, f.Admin, "Reason", version));
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_cancel BEFORE INSERT ON DocumentProcesses WHEN NEW.Action='Cancel' BEGIN SELECT RAISE(ABORT,'fixture audit failure'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Repository.CancelAsync(doc.Id, f.Admin, "Reason", doc.ConcurrencyVersion));
        f.Db.ChangeTracker.Clear();
        var saved = (await f.Repository.GetIncludingDeletedAsync(doc.Id))!;
        Assert.False(saved.IsDeleted); Assert.Equal(doc.ConcurrencyVersion, saved.ConcurrencyVersion);
        Assert.Single(saved.Processes); Assert.Equal(1, await f.Db.SignatureEvidence.CountAsync());
    }
}
