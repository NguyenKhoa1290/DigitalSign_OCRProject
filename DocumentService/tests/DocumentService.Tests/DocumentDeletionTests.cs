using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class DocumentDeletionTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public DocumentRepository Repository => new(Db);
        public readonly Guid Owner = Guid.NewGuid();
        public readonly Mock<IFileStorageService> Storage = new();
        public Service Service => new(Repository, new DocumentTypeRepository(Db), new DocumentProcessRepository(Db),
            Storage.Object, Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        public DocumentActor Actor(string role, bool own = true) => new(own ? Owner : Guid.NewGuid(), [role]);
        public async Task<Document> Seed()
        {
            await Connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            await Db.Database.EnsureCreatedAsync();
            await Db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT NOT NULL)");
            var document = new Document
            {
                Id = Guid.NewGuid(), Title = "Bản nháp thử nghiệm", DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                CreatedById = Owner, CreatedAt = DateTime.UtcNow, DeletionEligibilityConfirmed = true,
                FileName = "Bản gốc.pdf", MinioPath = "documents/original.pdf"
            };
            Db.Add(document);
            Db.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = document.Id, FromUserId = Owner, Action = DocumentAction.Create });
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
            return document;
        }
        public async ValueTask DisposeAsync() { if (Db != null) await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Specialist", true, true)]
    [InlineData("Clerk", true, true)]
    [InlineData("Admin", false, true)]
    [InlineData("Specialist", false, false)]
    [InlineData("Clerk", false, false)]
    [InlineData("Manager", true, false)]
    [InlineData("BoardOfDirectors", true, false)]
    public async Task RoleAndOwnershipAreEnforced(string role, bool own, bool allowed)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        Func<Task> act = () => f.Service.DeleteDocumentAsync(doc.Id, f.Actor(role, own), "Nhập trùng");
        if (allowed) await act();
        else await act.Should().ThrowAsync<UnauthorizedDocumentAccessException>();
        f.Db.ChangeTracker.Clear();
        (await f.Repository.GetIncludingDeletedAsync(doc.Id))!.IsDeleted.Should().Be(allowed);
        f.Storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("PendingDeptReview")]
    [InlineData("DeptSigned")]
    [InlineData("PendingDirectorSign")]
    [InlineData("DirectorSigned")]
    [InlineData("Rejected")]
    [InlineData("Published")]
    public async Task AdminCannotBypassWorkflowStage(string status)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await f.Db.Documents.Where(d => d.Id == doc.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.Status, status));
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Admin"), "Hỗ trợ"))
            .Should().ThrowAsync<DocumentConflictException>();
        (await f.Repository.GetByIdAsync(doc.Id)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Submit")]
    [InlineData("Assign")]
    [InlineData("DeptSign")]
    [InlineData("SubmitDirector")]
    [InlineData("DirectorSign")]
    [InlineData("Reject")]
    [InlineData("Publish")]
    [InlineData("Receive")]
    public async Task ReturningToDraftDoesNotEraseHistory(string action)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        f.Db.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = doc.Id, FromUserId = f.Owner, Action = action });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Admin"), "Hỗ trợ"))
            .Should().ThrowAsync<DocumentConflictException>();
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("owner")]
    [InlineData("submit")]
    [InlineData("assign")]
    [InlineData("receive")]
    [InlineData("signing")]
    [InlineData("signature")]
    public async Task MissingProvenanceAndBusinessMilestonesBlockDeletion(string condition)
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        var stored = (await f.Db.Documents.SingleAsync());
        switch (condition)
        {
            case "legacy": stored.DeletionEligibilityConfirmed = false; break;
            case "owner": stored.CreatedById = null; break;
            case "submit": stored.FirstSubmittedAt = DateTime.UtcNow; break;
            case "assign": stored.FirstAssignedAt = DateTime.UtcNow; break;
            case "receive": stored.OfficiallyReceivedAt = DateTime.UtcNow; break;
            case "signing": stored.SigningStartedAt = DateTime.UtcNow; break;
            case "signature": await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures (DocId) VALUES ({doc.Id})"); break;
        }
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Admin"), "Hỗ trợ"))
            .Should().ThrowAsync<DocumentConflictException>();
    }

    [Fact]
    public async Task SoftDeleteHidesEveryNormalReadAndWrite_AndRestorePreservesHistoryAndFile()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        var stale = (await f.Repository.GetByIdAsync(doc.Id))!;
        await f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Specialist"), "Nhập nhầm");
        f.Db.ChangeTracker.Clear();
        await f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Specialist"), "Lặp lại");
        (await f.Repository.GetAllAsync(new())).Total.Should().Be(0);
        (await f.Service.GetStatisticsAsync(f.Owner)).TotalDocuments.Should().Be(0);
        (await f.Repository.GetByIdAsync(doc.Id)).Should().BeNull();
        (await f.Repository.ExistsAsync(doc.Id)).Should().BeFalse();
        await FluentActions.Invoking(() => f.Service.GetFileAsync(doc.Id)).Should().ThrowAsync<DocumentNotFoundException>();
        await FluentActions.Invoking(() => f.Service.UpdateOcrDataAsync(doc.Id, new(), f.Owner)).Should().ThrowAsync<DocumentNotFoundException>();
        await FluentActions.Invoking(() => f.Service.UploadFileAsync(doc.Id, Stream.Null, "new.pdf", "application/pdf", f.Owner)).Should().ThrowAsync<DocumentNotFoundException>();
        await FluentActions.Invoking(() => f.Service.SubmitForReviewAsync(doc.Id, f.Owner, null)).Should().ThrowAsync<DocumentNotFoundException>();
        await FluentActions.Invoking(() => f.Service.AssignAsync(doc.Id, f.Owner, Guid.NewGuid(), null)).Should().ThrowAsync<DocumentNotFoundException>();
        (await f.Repository.ReplaceFileAsync(doc.Id, doc.MinioPath, "documents/new.pdf", "new.pdf")).Should().BeFalse();
        await FluentActions.Invoking(() => f.Repository.UpdateAsync(stale)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        f.Db.ChangeTracker.Clear();
        (await f.Service.GetDeletedDocumentsAsync(new(), f.Actor("Admin"))).TotalCount.Should().Be(1);
        f.Storage.Setup(s => s.DownloadFileAsync("original.pdf", "documents")).ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        await f.Service.RestoreDocumentAsync(doc.Id, f.Actor("Admin"), "Đã đối chiếu");
        f.Db.ChangeTracker.Clear();
        await f.Service.RestoreDocumentAsync(doc.Id, f.Actor("Admin"), "Lặp lại");
        var restored = (await f.Repository.GetByIdAsync(doc.Id, true))!;
        restored.CreatedById.Should().Be(f.Owner); restored.Status.Should().Be("Draft");
        restored.MinioPath.Should().Be(doc.MinioPath); restored.FileName.Should().Be(doc.FileName);
        restored.Processes.Count(p => p.Action == "SoftDelete").Should().Be(1);
        restored.Processes.Count(p => p.Action == "Restore").Should().Be(1);
        restored.Processes.Single(p => p.Action == "SoftDelete").Comment.Should().Be("Nhập nhầm");
        f.Storage.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        await FluentActions.Invoking(() => f.Service.UpdateOcrDataAsync(doc.Id, new() { ExpectedVersion = stale.ConcurrencyVersion }, f.Owner))
            .Should().ThrowAsync<DocumentConflictException>();
    }

    [Fact]
    public async Task AdminReasonAndRestoreAuthorityAreRequired_EvenForRepeatedRequests()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Admin"), " "))
            .Should().ThrowAsync<DocumentServiceException>();
        await f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Clerk"), null); f.Db.ChangeTracker.Clear();
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Specialist", false), null))
            .Should().ThrowAsync<UnauthorizedDocumentAccessException>();
        await FluentActions.Invoking(() => f.Service.RestoreDocumentAsync(doc.Id, f.Actor("Clerk"), "Khôi phục"))
            .Should().ThrowAsync<UnauthorizedDocumentAccessException>();
        await FluentActions.Invoking(() => f.Service.GetDeletedDocumentsAsync(new(), f.Actor("Specialist")))
            .Should().ThrowAsync<UnauthorizedDocumentAccessException>();
        await FluentActions.Invoking(() => f.Service.RestoreDocumentAsync(doc.Id, f.Actor("Admin"), null))
            .Should().ThrowAsync<DocumentServiceException>();
        f.Storage.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new IOException());
        await FluentActions.Invoking(() => f.Service.RestoreDocumentAsync(doc.Id, f.Actor("Admin"), "Khôi phục"))
            .Should().ThrowAsync<DocumentConflictException>();
        (await f.Repository.GetIncludingDeletedAsync(doc.Id))!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task OfficialReceiptPreventsDeletionAndIncomingCannotUseUnsignedAssignment()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await f.Service.ReceiveDocumentAsync(doc.Id, f.Actor("Clerk")); f.Db.ChangeTracker.Clear();
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Admin"), "Hỗ trợ"))
            .Should().ThrowAsync<DocumentConflictException>();
        var document = (await f.Repository.GetByIdAsync(doc.Id))!;
        document.OfficiallyReceivedAt.Should().NotBeNull();
        await FluentActions.Invoking(() => f.Service.AssignAsync(doc.Id, f.Owner, Guid.NewGuid(), null))
            .Should().ThrowAsync<DocumentConflictException>();
        (await f.Repository.GetByIdAsync(doc.Id))!.FirstAssignedAt.Should().BeNull();
    }

    [Fact]
    public async Task AuditFailureRollsBackDeletion()
    {
        await using var f = new Fixture(); var doc = await f.Seed();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_audit BEFORE INSERT ON DocumentProcesses BEGIN SELECT RAISE(ABORT, 'fixture'); END");
        await FluentActions.Invoking(() => f.Service.DeleteDocumentAsync(doc.Id, f.Actor("Clerk"), null))
            .Should().ThrowAsync<DbUpdateException>();
        f.Db.ChangeTracker.Clear();
        (await f.Repository.GetByIdAsync(doc.Id)).Should().NotBeNull();
        (await f.Db.DocumentProcesses.CountAsync()).Should().Be(1);
    }
}
