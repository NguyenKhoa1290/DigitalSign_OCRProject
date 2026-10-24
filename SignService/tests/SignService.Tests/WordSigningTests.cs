using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SignService.Core.DTOs;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;

namespace SignService.Tests;

public class WordSigningTests
{
    [Fact]
    public async Task WordCannotStartPdfSigningOrTouchStorage()
    {
        var id = Guid.NewGuid();
        var files = new Mock<IDocumentFileRepository>();
        files.Setup(f => f.GetMinioPathAsync(id)).ReturnsAsync("documents/edited.docx");
        var certificates = new Mock<ICertificateService>();
        certificates.Setup(c => c.GetUserCertificateAsync(It.IsAny<Guid>())).ReturnsAsync(new UserCertificate());
        certificates.Setup(c => c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(true);
        var storage = new Mock<IMinioService>();
        var service = new Infrastructure.Services.SignService(Mock.Of<ISignatureRepository>(), certificates.Object,
            Mock.Of<IPdfSigningService>(), storage.Object, files.Object,
            new ConfigurationBuilder().Build(), NullLogger<Infrastructure.Services.SignService>.Instance);
        var exception = await Assert.ThrowsAsync<SignServiceException>(() => service.PersonalSignAsync(new SignRequestDto { DocId = id, SignerId = Guid.NewGuid() }));
        Assert.Contains("PDF", exception.Message);
        files.Verify(f => f.BeginSigningAsync(It.IsAny<Guid>()), Times.Never);
        storage.Verify(f => f.DownloadFileAsync(It.IsAny<string>()), Times.Never);
        await Assert.ThrowsAsync<SignServiceException>(() => service.VerifySignaturesAsync(id));
        storage.Verify(f => f.DownloadFileAsync(It.IsAny<string>()), Times.Never);
    }
}
