using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class DocumentFileReplacementTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IFileStorageService> _files = new();
    private readonly Mock<IKafkaProducerService> _kafka = new();
    private readonly Document _document = new()
    {
        Id = Guid.NewGuid(), Title = "Replacement fixture", MinioPath = "documents/old.pdf",
        FileName = "Công văn cũ.pdf"
    };
    private readonly Service _service;
    private const string OriginalName = "Kế hoạch tốt nghiệp 2026.pdf";

    public DocumentFileReplacementTests()
    {
        _documents.Setup(r => r.GetByIdAsync(_document.Id, It.IsAny<bool>())).ReturnsAsync(_document);
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/new.pdf", OriginalName))
            .ReturnsAsync(true);
        _files.Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), OriginalName, "application/pdf", "documents"))
            .ReturnsAsync("new.pdf");
        _files.Setup(f => f.DeleteFileAsync(It.IsAny<string>(), "documents")).ReturnsAsync(true);
        _service = new Service(_documents.Object, Mock.Of<IDocumentTypeRepository>(),
            Mock.Of<IDocumentProcessRepository>(), _files.Object, _kafka.Object, NullLogger<Service>.Instance);
    }

    private async Task<Core.DTOs.DocumentDto> Upload(string name = OriginalName)
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        return await _service.UploadFileAsync(_document.Id, content, name, "application/pdf", Guid.NewGuid());
    }

    [Theory]
    [InlineData(OriginalName)]
    [InlineData("C:\\fakepath\\" + OriginalName)]
    [InlineData("/uploads/" + OriginalName)]
    public async Task Replacement_SwitchesPointerBeforeDeletingOldObject_AndKeepsOriginalName(string suppliedName)
    {
        var saved = false;
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/new.pdf", OriginalName))
            .Callback(() => saved = true).ReturnsAsync(true);
        _files.Setup(f => f.DeleteFileAsync("old.pdf", "documents"))
            .Callback(() => saved.Should().BeTrue()).ReturnsAsync(true);
        var result = await Upload(suppliedName);
        result.FileName.Should().Be(OriginalName);
        result.MinioPath.Should().Be("documents/new.pdf");
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Once);
        _files.Verify(f => f.DeleteFileAsync("new.pdf", "documents"), Times.Never);
        _documents.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
        _kafka.Verify(k => k.PublishDocumentUploadedAsync(_document.Id, "new.pdf", ""), Times.Once);
        var file = await _service.GetFileAsync(_document.Id);
        file.FileName.Should().Be(OriginalName);
    }

    [Fact]
    public async Task FirstUpload_DoesNotDeleteAnyObject()
    {
        _document.MinioPath = "";
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "", "documents/new.pdf", OriginalName)).ReturnsAsync(true);
        await Upload();
        _files.Verify(f => f.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StorageFailure_LeavesOldPdfAndNameUntouched()
    {
        _files.Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), OriginalName, "application/pdf", "documents"))
            .ThrowsAsync(new IOException("Storage unavailable"));
        await FluentActions.Awaiting(() => Upload()).Should().ThrowAsync<IOException>();
        _document.MinioPath.Should().Be("documents/old.pdf");
        _document.FileName.Should().Be("Công văn cũ.pdf");
        _documents.Verify(r => r.ReplaceFileAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _files.Verify(f => f.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DatabaseFailure_DoesNotDeleteEitherObjectWithUnknownCommitOutcome()
    {
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/new.pdf", OriginalName))
            .ThrowsAsync(new IOException("Database unavailable"));
        await FluentActions.Awaiting(() => Upload()).Should().ThrowAsync<IOException>();
        _files.Verify(f => f.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kafka.Verify(k => k.PublishDocumentUploadedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentReplacement_DeletesOnlyRejectedNewObject()
    {
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/new.pdf", OriginalName))
            .ReturnsAsync(false);
        await FluentActions.Awaiting(() => Upload()).Should().ThrowAsync<DocumentServiceException>();
        _files.Verify(f => f.DeleteFileAsync("new.pdf", "documents"), Times.Once);
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Never);
        _kafka.Verify(k => k.PublishDocumentUploadedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Cleanup_RetriesFalseAndException_WithoutLosingNewPdf()
    {
        _files.SetupSequence(f => f.DeleteFileAsync("old.pdf", "documents"))
            .ReturnsAsync(false).ThrowsAsync(new IOException("Temporary storage failure")).ReturnsAsync(true);
        (await Upload()).FileName.Should().Be(OriginalName);
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Exactly(3));
        _files.Verify(f => f.DeleteFileAsync("new.pdf", "documents"), Times.Never);
    }

    [Fact]
    public async Task Cleanup_UnavailableStorage_DoesNotFailSuccessfulReplacement()
    {
        _files.Setup(f => f.DeleteFileAsync("old.pdf", "documents")).ReturnsAsync(false);
        (await Upload()).MinioPath.Should().Be("documents/new.pdf");
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Exactly(3));
    }

    [Fact]
    public async Task LegacyPdf_UsesFallbackFilename()
    {
        _document.FileName = null;
        (await _service.GetFileAsync(_document.Id)).FileName.Should().Be($"cong-van-{_document.Id}.pdf");
    }

    [Theory]
    [InlineData("Ảnh công văn.PNG", "image/png")]
    [InlineData("Ảnh công văn.jpg", "image/jpeg")]
    [InlineData("Ảnh công văn.jpeg", "image/jpeg")]
    [InlineData("Ảnh công văn.webp", "image/webp")]
    public async Task ImageUpload_UsesImageMime_AndDownloadPreservesOriginalName(string name, string mime)
    {
        var objectName = "new" + Path.GetExtension(name);
        _files.Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), name, mime, "documents")).ReturnsAsync(objectName);
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/" + objectName, name)).ReturnsAsync(true);
        var result = await Upload(name); // Simulates an incorrect PDF header; type comes from allowed extension.
        result.FileContentType.Should().Be(mime);
        result.FileName.Should().Be(name);
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Once);
        var downloaded = await _service.GetFileAsync(_document.Id);
        downloaded.ContentType.Should().Be(mime);
        downloaded.FileName.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("document.doc")]
    [InlineData("bad\r\nname.pdf")]
    [InlineData("document.svg")]
    [InlineData("document.exe")]
    public async Task InvalidName_RejectsBeforeUploading(string name)
    {
        await FluentActions.Awaiting(() => Upload(name)).Should().ThrowAsync<DocumentServiceException>();
        _files.Verify(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("Công văn đã sửa.docx")]
    [InlineData("Công văn đã sửa.DOCX")]
    public async Task WordReplacement_DownloadsWithOriginalNameAndMime_WithoutQueuingOcr(string name)
    {
        const string mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        _files.Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), name, mime, "documents")).ReturnsAsync("edited.docx");
        _documents.Setup(r => r.ReplaceFileAsync(_document.Id, "documents/old.pdf", "documents/edited.docx", name)).ReturnsAsync(true);
        var result = await Upload(name);
        result.FileContentType.Should().Be(mime);
        result.FileName.Should().Be(name);
        _files.Verify(f => f.DeleteFileAsync("old.pdf", "documents"), Times.Once);
        _kafka.Verify(k => k.PublishDocumentUploadedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        var download = await _service.GetFileAsync(_document.Id);
        download.FileName.Should().Be(name);
        download.ContentType.Should().Be(mime);
    }
}
