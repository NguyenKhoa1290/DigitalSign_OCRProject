using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Kernel.Pdf;
using iText.Signatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SignService.Core.Entities;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Services;

namespace SignService.Tests;

public class IncomingPdfSignatureTests
{
    [Fact]
    public async Task RealPdfSupportsBoardDirectionThenManagerAssignmentAndBindsInstructionsToEachSignature()
    {
        var board = Guid.NewGuid(); var manager = Guid.NewGuid();
        UserCertificate Certificate(Guid id, string name)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            return new UserCertificate { UserId = id, FullName = name, CertificatePfx = cert.Export(X509ContentType.Pkcs12, id.ToString()),
                NotBefore = DateTime.UtcNow.AddMinutes(-5), NotAfter = DateTime.UtcNow.AddDays(1) };
        }
        var certificates = new Dictionary<Guid, UserCertificate> { [board] = Certificate(board, "Board fixture"), [manager] = Certificate(manager, "Manager fixture") };
        var service = new Mock<ICertificateService>();
        service.Setup(c => c.GetUserCertificateAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => certificates[id]);
        service.Setup(c => c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(true);
        var pdf = new PdfSigningService(service.Object, NullLogger<PdfSigningService>.Instance);
        using var original = new MemoryStream();
        using (var document = new PdfDocument(new PdfWriter(original))) { document.AddNewPage(); }
        var direction = "Chỉ đạo Khoa fixture triển khai nhiệm vụ";
        var assignment = "Phân công chuyên viên fixture báo cáo kết quả";
        var first = await pdf.SignPdfAsync(original.ToArray(), board, SignatureType.IncomingDirection, "Board fixture", direction);
        var second = await pdf.SignPdfAsync(first, manager, SignatureType.IncomingAssignment, "Manager fixture", assignment);
        var verified = await pdf.VerifyPdfSignaturesAsync(second);
        Assert.True(verified.IsValid);
        Assert.Equal(2, verified.SignatureCount);
        Assert.Equal(new[] { SignatureType.IncomingDirection, SignatureType.IncomingAssignment }, verified.Signatures.Select(s => s.SignatureType));
        Assert.All(verified.Signatures, signature => { Assert.True(signature.IsIntact); Assert.True(signature.IsCertificateValid); });
        using var final = new PdfDocument(new PdfReader(new MemoryStream(second)));
        var signatures = new SignatureUtil(final);
        var names = signatures.GetSignatureNames();
        Assert.Equal(direction, signatures.GetSignatureDictionary(names[0]).GetAsString(PdfName.Reason).ToUnicodeString());
        Assert.Equal(assignment, signatures.GetSignatureDictionary(names[1]).GetAsString(PdfName.Reason).ToUnicodeString());
    }
}
