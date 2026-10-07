using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class DocumentEditingTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IDocumentTypeRepository> _types = new();
    private readonly Mock<IDocumentProcessRepository> _processes = new();
    private readonly Mock<IFileStorageService> _files = new();
    private readonly Document _document = new()
    {
        Id = Guid.NewGuid(), Title = "Original", DocTypeId = Guid.NewGuid(),
        MinioPath = "documents/original.pdf", Status = DocumentStatus.Draft,
        OcrDataRaw = "{\"pages\":[]}"
    };
    private readonly Service _service;

    public DocumentEditingTests()
    {
        _documents.Setup(repo => repo.GetByIdAsync(_document.Id, It.IsAny<bool>())).ReturnsAsync(_document);
        _documents.Setup(repo => repo.UpdateAsync(It.IsAny<Document>())).ReturnsAsync((Document doc) => doc);
        _types.Setup(repo => repo.GetByIdAsync(_document.DocTypeId))
            .ReturnsAsync(new DocumentType { Id = _document.DocTypeId, TypeName = "CongVanDen" });
        _service = new Service(_documents.Object, _types.Object, _processes.Object, _files.Object,
            Mock.Of<IKafkaProducerService>(), NullLogger<Service>.Instance);
    }

    private UpdateDocumentDto ValidUpdate() => new()
    {
        Title = "  Corrected title  ", DocTypeId = _document.DocTypeId,
        DocNumber = " 123/CV ", IssuedDate = new DateOnly(2026, 10, 5)
    };

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Rejected)]
    public async Task Edit_AllowedStatus_PreservesFileOcrAndWorkflowAndRecordsActor(string status)
    {
        _document.Status = status;
        var actor = Guid.NewGuid();
        var result = await _service.UpdateDocumentAsync(_document.Id, ValidUpdate(), actor);
        result.Title.Should().Be("Corrected title");
        result.DocNumber.Should().Be("123/CV");
        result.IssuedDate.Should().Be(new DateOnly(2026, 10, 5));
        result.Status.Should().Be(status);
        result.MinioPath.Should().Be("documents/original.pdf");
        result.OcrDataRaw.Should().Be("{\"pages\":[]}");
        _processes.Verify(repo => repo.CreateAsync(It.Is<DocumentProcess>(p =>
            p.DocId == _document.Id && p.FromUserId == actor && p.Action == DocumentAction.Update)), Times.Once);
    }

    [Theory]
    [InlineData(DocumentStatus.PendingDeptReview)]
    [InlineData(DocumentStatus.DeptSigned)]
    [InlineData(DocumentStatus.PendingDirectorSign)]
    [InlineData(DocumentStatus.DirectorSigned)]
    [InlineData(DocumentStatus.Published)]
    public async Task Edit_AfterSubmission_RejectsWithoutMutation(string status)
    {
        _document.Status = status;
        await FluentActions.Awaiting(() => _service.UpdateDocumentAsync(_document.Id, ValidUpdate(), Guid.NewGuid()))
            .Should().ThrowAsync<InvalidWorkflowTransitionException>();
        _document.Title.Should().Be("Original");
        _documents.Verify(repo => repo.UpdateAsync(It.IsAny<Document>()), Times.Never);
        _processes.Verify(repo => repo.CreateAsync(It.IsAny<DocumentProcess>()), Times.Never);
    }

    [Fact]
    public async Task Edit_UnknownType_RejectsWithoutSaving()
    {
        var update = ValidUpdate(); update.DocTypeId = Guid.NewGuid();
        await FluentActions.Awaiting(() => _service.UpdateDocumentAsync(_document.Id, update, Guid.NewGuid()))
            .Should().ThrowAsync<DocumentServiceException>();
        _documents.Verify(repo => repo.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task Edit_ClearOptionalFields_DoesNotKeepOldValues()
    {
        _document.DocNumber = "Old"; _document.IssuedDate = new DateOnly(2026, 1, 1);
        var update = ValidUpdate(); update.DocNumber = " "; update.IssuedDate = null;
        var result = await _service.UpdateDocumentAsync(_document.Id, update, Guid.NewGuid());
        result.DocNumber.Should().BeNull(); result.IssuedDate.Should().BeNull();
    }

    [Theory]
    [InlineData("documents/actual-file.pdf", "actual-file.pdf")]
    [InlineData("/documents/actual-file.pdf", "actual-file.pdf")]
    [InlineData("actual-file.pdf", "actual-file.pdf")]
    public async Task File_UsesStoredObjectRatherThanDocumentId(string path, string expected)
    {
        _document.MinioPath = path;
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        _files.Setup(storage => storage.DownloadFileAsync(expected, "documents")).ReturnsAsync(stream);
        var result = await _service.GetFileAsync(_document.Id);
        result.Content.Should().BeSameAs(stream);
        result.FileName.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task File_WithoutUpload_IsNotFound()
    {
        _document.MinioPath = "";
        await FluentActions.Awaiting(() => _service.GetFileAsync(_document.Id)).Should().ThrowAsync<DocumentNotFoundException>();
        _files.Verify(storage => storage.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Edit_UnknownDocument_IsNotFound()
    {
        await FluentActions.Awaiting(() => _service.UpdateDocumentAsync(Guid.NewGuid(), ValidUpdate(), Guid.NewGuid()))
            .Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task Assign_EmptyRecipient_DoesNotRecordSuccess()
    {
        await FluentActions.Awaiting(() => _service.AssignAsync(_document.Id, Guid.NewGuid(), Guid.Empty, null))
            .Should().ThrowAsync<DocumentServiceException>();
        _processes.Verify(repo => repo.CreateAsync(It.IsAny<DocumentProcess>()), Times.Never);
    }

    [Fact]
    public async Task Assign_RecordsRecipientAndPreservesStatus()
    {
        var sender = Guid.NewGuid(); var recipient = Guid.NewGuid();
        var result = await _service.AssignAsync(_document.Id, sender, recipient, "Handle this");
        result.Status.Should().Be(DocumentStatus.Draft);
        _processes.Verify(repo => repo.CreateAsync(It.Is<DocumentProcess>(p =>
            p.FromUserId == sender && p.ToUserId == recipient && p.Comment == "Handle this" && p.Action == DocumentAction.Assign)), Times.Once);
    }
}
