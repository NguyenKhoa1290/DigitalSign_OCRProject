using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class IncomingDocumentWorkflowTests
{
    [Fact]
    public async Task OutgoingWorkflowStillRunsThroughTheOriginalFiveTransitions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var id = Guid.NewGuid(); var user = Guid.NewGuid();
        db.Add(new Document { Id = id, Title = "Outgoing fixture", DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), ConcurrencyVersion = Guid.NewGuid() });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new Service(new DocumentRepository(db), new DocumentTypeRepository(db), new DocumentProcessRepository(db),
            Mock.Of<IFileStorageService>(), Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        Assert.Equal(DocumentStatus.PendingDeptReview, (await service.SubmitForReviewAsync(id, user, null)).Status);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.DeptSigned, (await service.DeptSignAsync(id, user, null)).Status);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.PendingDirectorSign, (await service.SubmitToDirectorAsync(id, user, null)).Status);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.DirectorSigned, (await service.DirectorSignAsync(id, user, null)).Status);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Published, (await service.PublishAsync(id, user)).Status);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("dept-sign")]
    [InlineData("director-submit")]
    [InlineData("director-sign")]
    [InlineData("publish")]
    [InlineData("reject")]
    [InlineData("assign")]
    public async Task IncomingCannotUseAnyOfTheOutgoingWorkflowActions(string action)
    {
        var id = Guid.NewGuid(); var user = Guid.NewGuid();
        var repository = new Mock<IDocumentRepository>();
        repository.Setup(r => r.GetByIdAsync(id, false)).ReturnsAsync(new Document { Id = id, DocType = new() { TypeName = "CongVanDen" } });
        var service = new Service(repository.Object, Mock.Of<IDocumentTypeRepository>(), Mock.Of<IDocumentProcessRepository>(),
            Mock.Of<IFileStorageService>(), Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        Func<Task> call = action switch {
            "submit" => () => service.SubmitForReviewAsync(id, user, null),
            "dept-sign" => () => service.DeptSignAsync(id, user, null),
            "director-submit" => () => service.SubmitToDirectorAsync(id, user, null),
            "director-sign" => () => service.DirectorSignAsync(id, user, null),
            "publish" => () => service.PublishAsync(id, user),
            "reject" => () => service.RejectAsync(id, user, "Lý do"),
            _ => () => service.AssignAsync(id, user, Guid.NewGuid(), null)
        };
        await Assert.ThrowsAsync<DocumentConflictException>(call);
        repository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task SubmitRequiresTheClerkRoleAndDoesNotAcceptRolesFromTheRequestBody()
    {
        var repository = new Mock<IDocumentRepository>();
        var service = new Service(repository.Object, Mock.Of<IDocumentTypeRepository>(), Mock.Of<IDocumentProcessRepository>(),
            Mock.Of<IFileStorageService>(), Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        foreach (var role in new[] { "Specialist", "Manager", "BoardOfDirectors", "Admin" })
            await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(() => service.SubmitIncomingAsync(Guid.NewGuid(), new(Guid.NewGuid(), [role]), Guid.NewGuid(), null));
        repository.Verify(r => r.SubmitIncomingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }
}
