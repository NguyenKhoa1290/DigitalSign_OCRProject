using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class DocumentCreationMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalIssuedDateAndNumber_DoNotAffectOriginalCreationTimestamp(bool withIssuedDate)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var typeId = Guid.NewGuid();
        context.DocumentTypes.Add(new DocumentType { Id = typeId, TypeName = "CongVanDen" });
        await context.SaveChangesAsync();
        var processes = new Mock<IDocumentProcessRepository>();
        processes.Setup(repo => repo.CreateAsync(It.IsAny<DocumentProcess>())).Returns(async (DocumentProcess process) =>
        {
            process.Id = Guid.NewGuid();
            context.DocumentProcesses.Add(process);
            await context.SaveChangesAsync();
            return process;
        });
        var service = new Service(new DocumentRepository(context), new DocumentTypeRepository(context),
            processes.Object, Mock.Of<IFileStorageService>(),
            Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        var started = DateTime.UtcNow;
        var created = await service.CreateDocumentAsync(new CreateDocumentDto
        {
            Title = "Optional metadata", DocTypeId = typeId,
            IssuedDate = withIssuedDate ? new DateOnly(2026, 10, 14) : null, DocNumber = null
        }, Guid.NewGuid());
        created.DocNumber.Should().BeNull();
        created.IssuedDate.Should().Be(withIssuedDate ? new DateOnly(2026, 10, 14) : null);
        created.CreatedAt.Should().NotBeNull();
        created.CreatedAt!.Value.Should().BeOnOrAfter(started).And.BeOnOrBefore(DateTime.UtcNow);

        // Editing occurs in a new HTTP request with a fresh change tracker.
        context.ChangeTracker.Clear();
        var updated = await service.UpdateDocumentAsync(created.Id, new UpdateDocumentDto
        {
            Title = created.Title, DocTypeId = typeId, DocNumber = "123/CV", IssuedDate = new DateOnly(2026, 10, 15)
        }, Guid.NewGuid());
        updated.DocNumber.Should().Be("123/CV");
        updated.CreatedAt.Should().Be(created.CreatedAt);
        updated.UpdatedAt.Should().Be(updated.Processes.Max(process => process.Timestamp));

        var list = await service.GetAllDocumentsAsync(new DocumentQueryParams());
        list.Items.Single().CreatedAt.Should().Be(created.CreatedAt);
        list.Items.Single().IssuedDate.Should().Be(new DateOnly(2026, 10, 15));
    }

    [Fact]
    public async Task LegacyWithoutCreationHistory_DoesNotInventDate()
    {
        var document = new Document { Id = Guid.NewGuid(), Title = "Legacy", DocTypeId = Guid.NewGuid() };
        var repository = new Mock<IDocumentRepository>();
        repository.Setup(repo => repo.GetByIdAsync(document.Id, true)).ReturnsAsync(document);
        var service = new Service(repository.Object, Mock.Of<IDocumentTypeRepository>(),
            Mock.Of<IDocumentProcessRepository>(), Mock.Of<IFileStorageService>(),
            Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
        var result = await service.GetDocumentByIdAsync(document.Id);
        result.CreatedAt.Should().BeNull(); result.UpdatedAt.Should().BeNull();
    }
}
