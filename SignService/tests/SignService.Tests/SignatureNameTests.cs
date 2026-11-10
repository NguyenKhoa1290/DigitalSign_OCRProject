using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SignService.Core.Entities;
using SignService.Core.Interfaces;
using SigningService = SignService.Infrastructure.Services.SignService;

namespace SignService.Tests;

public class SignatureNameTests
{
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Mock<ISignatureRepository> _signatures = new();
    private readonly Mock<ICertificateService> _certificates = new();
    private readonly Mock<IPdfSigningService> _pdf = new();
    private readonly Mock<IMinioService> _storage = new();
    private readonly Mock<IDocumentFileRepository> _documents = new();

    private SigningService CreateService(params Signature[] signatures)
    {
        _documents.Setup(repository => repository.GetMinioPathAsync(_documentId))
            .ReturnsAsync($"{_documentId}/signed.pdf");
        _signatures.Setup(repository => repository.GetByDocumentIdAsync(_documentId))
            .ReturnsAsync(signatures);
        return new SigningService(_signatures.Object, _certificates.Object, _pdf.Object,
            _storage.Object, _documents.Object, new ConfigurationBuilder().Build(),
            NullLogger<SigningService>.Instance);
    }

    [Fact]
    public async Task ReturnsEmbeddedNamesForEachStepEvenWhenCertificatesAreRevoked()
    {
        var manager = new Signature { Id = Guid.NewGuid(), DocId = _documentId,
            SignerId = Guid.NewGuid(), SignatureType = SignatureType.PersonalSignature };
        var board = new Signature { Id = Guid.NewGuid(), DocId = _documentId,
            SignerId = Guid.NewGuid(), SignatureType = SignatureType.LegalSeal };
        var service = CreateService(manager, board);
        _storage.Setup(storage => storage.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 1 });
        _pdf.Setup(pdf => pdf.VerifyPdfSignaturesAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new SignVerifyResult { IsValid = false, Signatures =
            [
                new SignatureInfo { SignatureType = SignatureType.LegalSeal,
                    SignerName = "Nguyễn Văn An", IsIntact = true, IsRevoked = true },
                new SignatureInfo { SignatureType = SignatureType.PersonalSignature,
                    SignerName = "Trần Trung Hoàng", IsIntact = true, IsRevoked = true }
            ] });

        var result = (await service.GetSignaturesByDocumentAsync(_documentId)).ToArray();

        Assert.Equal("Trần Trung Hoàng", result[0].SignerName);
        Assert.Equal(manager.SignerId, result[0].SignerId);
        Assert.Equal("Nguyễn Văn An", result[1].SignerName);
        _certificates.Verify(certificate => certificate.GetUserCertificateAsync(It.IsAny<Guid>()), Times.Never);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("Trần Trung Hoàng", json.RootElement[0].GetProperty("signerName").GetString());
    }

    [Fact]
    public async Task UsesStoredCertificateNameWhenPdfCannotBeRead()
    {
        var signature = new Signature { SignerId = Guid.NewGuid(),
            SignatureType = SignatureType.PersonalSignature };
        var service = CreateService(signature);
        _storage.Setup(storage => storage.DownloadFileAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException("Fixture storage unavailable"));
        _certificates.Setup(certificate => certificate.GetUserCertificateAsync(signature.SignerId))
            .ReturnsAsync(new UserCertificate { FullName = "Trần Trung Hoàng" });

        var result = Assert.Single(await service.GetSignaturesByDocumentAsync(_documentId));

        Assert.Equal("Trần Trung Hoàng", result.SignerName);
        Assert.Equal(signature.SignerId, result.SignerId);
    }

    [Fact]
    public async Task DoesNotGuessBetweenMultiplePdfSignersForTheSameStep()
    {
        var signature = new Signature { SignerId = Guid.NewGuid(),
            SignatureType = SignatureType.PersonalSignature };
        var service = CreateService(signature);
        _storage.Setup(storage => storage.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 1 });
        _pdf.Setup(pdf => pdf.VerifyPdfSignaturesAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new SignVerifyResult { Signatures =
            [
                new SignatureInfo { SignatureType = signature.SignatureType,
                    SignerName = "Tên thứ nhất", IsIntact = true },
                new SignatureInfo { SignatureType = signature.SignatureType,
                    SignerName = "Tên thứ hai", IsIntact = true }
            ] });
        _certificates.Setup(certificate => certificate.GetUserCertificateAsync(signature.SignerId))
            .ReturnsAsync(new UserCertificate { FullName = "Trần Trung Hoàng" });

        var result = Assert.Single(await service.GetSignaturesByDocumentAsync(_documentId));

        Assert.Equal("Trần Trung Hoàng", result.SignerName);
    }

    [Fact]
    public async Task EmptyHistoryDoesNotReadPdfOrCertificates()
    {
        var service = CreateService();

        Assert.Empty(await service.GetSignaturesByDocumentAsync(_documentId));

        _storage.Verify(storage => storage.DownloadFileAsync(It.IsAny<string>()), Times.Never);
        _certificates.Verify(certificate => certificate.GetUserCertificateAsync(It.IsAny<Guid>()), Times.Never);
    }
}
