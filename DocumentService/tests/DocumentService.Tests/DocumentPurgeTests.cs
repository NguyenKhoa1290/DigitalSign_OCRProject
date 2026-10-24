using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentService.Tests;

public class DocumentPurgeTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db = null!;
        public Mock<IFileStorageService> Files = new();
        public DocumentActor Admin = new(Guid.NewGuid(), ["Admin"]);
        public DocumentPurgeService Service => new(Db, Files.Object, NullLogger<DocumentPurgeService>.Instance);
        public DocumentPurgeDto Request(Document doc) => new() { Reason = "Wrong record", ConfirmationTitle = doc.Title, ExpectedVersion = doc.ConcurrencyVersion };
        public async Task<Document> Seed(bool deleted = true)
        {
            await Connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            await Db.Database.EnsureCreatedAsync();
            await Db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT NOT NULL)");
            var doc = new Document { Id = Guid.NewGuid(), Title = "Purge fixture", IsDeleted = deleted, Status = "Published",
                DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), MinioPath = "documents/final.pdf",
                IncomingOriginalPath = "documents/original.pdf", IncomingDirectionPath = "documents/final.pdf", ConcurrencyVersion = Guid.Empty };
            Db.Add(doc);
            Db.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = doc.Id, Action = "Cancel", Timestamp = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            await Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures (DocId) VALUES ({doc.Id})");
            Db.ChangeTracker.Clear();
            Files.Setup(f => f.DeleteFileAsync(It.IsAny<string>(), "documents")).ReturnsAsync(true);
            return doc;
        }
        public async ValueTask DisposeAsync() { if (Db != null) await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task PurgeDeletesSignedCancelledRecord_AndRetainsIndependentAudit_WithIdempotentRetry()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        var result = await f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc));
        Assert.False(result.FilesPendingCleanup);
        Assert.Empty(await f.Db.Documents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.DocumentProcesses.ToListAsync());
        Assert.Empty(await f.Db.SignatureEvidence.ToListAsync());
        var audit = await f.Db.DocumentPurgeRecords.SingleAsync();
        Assert.Equal(doc.Id, audit.DocId); Assert.Equal(f.Admin.UserId, audit.ActorId); Assert.Equal("Wrong record", audit.Reason);
        Assert.NotNull(audit.FilesCleanedAt);
        f.Files.Verify(s => s.DeleteFileAsync("final.pdf", "documents"), Times.Once);
        f.Files.Verify(s => s.DeleteFileAsync("original.pdf", "documents"), Times.Once);
        await f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc));
        Assert.Single(await f.Db.DocumentPurgeRecords.ToListAsync());
        f.Files.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), "documents"), Times.Exactly(2));
    }

    [Theory]
    [InlineData("Specialist")]
    [InlineData("Clerk")]
    [InlineData("Manager")]
    [InlineData("BoardOfDirectors")]
    public async Task NonAdminCannotPurge(string role)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(() => f.Service.PurgeAsync(doc.Id, new(Guid.NewGuid(), [role]), f.Request(doc)));
        Assert.Single(await f.Db.Documents.IgnoreQueryFilters().ToListAsync());
        f.Files.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ActiveDocumentCannotBePurged()
    {
        await using var f = new Fixture(); var doc = await f.Seed(false);
        await Assert.ThrowsAsync<DocumentConflictException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc)));
        Assert.Empty(await f.Db.DocumentPurgeRecords.ToListAsync());
    }

    [Fact]
    public async Task MissingVersionWrongTitleBlankReasonAndStaleVersionNeverDeleteFiles()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        var request = f.Request(doc); request.ExpectedVersion = null;
        await Assert.ThrowsAsync<DocumentServiceException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, request));
        request = f.Request(doc); request.ConfirmationTitle = "Other title";
        await Assert.ThrowsAsync<DocumentServiceException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, request));
        request = f.Request(doc); request.Reason = " ";
        await Assert.ThrowsAsync<DocumentServiceException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, request));
        request = f.Request(doc); request.ExpectedVersion = Guid.NewGuid();
        await Assert.ThrowsAsync<DocumentConflictException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, request));
        Assert.Single(await f.Db.Documents.IgnoreQueryFilters().ToListAsync());
        f.Files.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnavailableStorageQueuesCleanup_AndLaterRetries()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        f.Files.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), "documents")).ReturnsAsync(false);
        Assert.True((await f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc))).FilesPendingCleanup);
        Assert.Null((await f.Db.DocumentPurgeRecords.SingleAsync()).FilesCleanedAt);
        f.Files.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), "documents")).ReturnsAsync(true);
        await f.Service.CleanupPendingAsync();
        Assert.NotNull((await f.Db.DocumentPurgeRecords.SingleAsync()).FilesCleanedAt);
    }

    [Fact]
    public async Task FilesReferencedByAnotherDocumentArePreserved()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        f.Db.Add(new Document { Id = Guid.NewGuid(), Title = "Shared file", MinioPath = "final.pdf", IsDeleted = true, DocTypeId = doc.DocTypeId });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc));
        f.Files.Verify(s => s.DeleteFileAsync("final.pdf", "documents"), Times.Never);
        f.Files.Verify(s => s.DeleteFileAsync("original.pdf", "documents"), Times.Once);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackEvidence_AndNeverTouchesStorage()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_purge BEFORE INSERT ON DocumentPurgeRecords BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.PurgeAsync(doc.Id, f.Admin, f.Request(doc)));
        f.Db.ChangeTracker.Clear();
        Assert.Single(await f.Db.Documents.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await f.Db.SignatureEvidence.ToListAsync());
        f.Files.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
