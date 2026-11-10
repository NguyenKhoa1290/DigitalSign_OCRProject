using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moq;
using SignService.Core.Interfaces;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Infrastructure.Services;

namespace SignService.Tests;

public class PdfVerificationIntegrityTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string folder=Path.Combine(Path.GetTempPath(),"hau-pdf-test-"+Guid.NewGuid().ToString("N"));
        public CertificateService Certificates { get; }
        public PdfSigningService Pdf { get; }
        public Guid Manager { get; }=Guid.NewGuid();
        public Fixture()
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CertificateSettings:CertsDirectory"]=folder }).Build();
            Certificates=new(config,NullLogger<CertificateService>.Instance);
            Pdf=new(Certificates,NullLogger<PdfSigningService>.Instance);
        }
        public async Task Initialize()
        { await Certificates.InitializeRootCaAsync();await Certificates.IssueCertificateAsync(Manager,"fixture","Fixture Manager"); }
        public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    private static byte[] Source()
    {
        using var output=new MemoryStream();
        using(var doc=new PdfDocument(new PdfWriter(output))){doc.AddNewPage();doc.GetDocumentInfo().SetTitle("AUDIT_ORIGINAL");}
        return output.ToArray();
    }

    [Fact]
    public async Task SequentialSignaturesRemainValidButAppendingUnsignedContentInvalidatesCurrentFile()
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var first=await fixture.Pdf.SignPdfAsync(Source(),fixture.Manager,SignatureType.PersonalSignature,"Manager","First");
        var board=Guid.NewGuid();await fixture.Certificates.IssueCertificateAsync(board,"fixture-board","Fixture Board");
        var second=await fixture.Pdf.SignPdfAsync(first,board,SignatureType.LegalSeal,"Board","Second");
        var valid=await fixture.Pdf.VerifyPdfSignaturesAsync(second);
        Assert.True(valid.IsValid);Assert.True(valid.CoversWholeDocument);Assert.Equal(2,valid.SignatureCount);
        using var changed=new MemoryStream();
        using(var doc=new PdfDocument(new PdfReader(new MemoryStream(second)),new PdfWriter(changed),new StampingProperties().UseAppendMode()))doc.AddNewPage();
        var invalid=await fixture.Pdf.VerifyPdfSignaturesAsync(changed.ToArray());
        Assert.False(invalid.IsValid);Assert.False(invalid.CoversWholeDocument);
        Assert.All(invalid.Signatures,s=>Assert.True(s.IsIntact));
        Assert.Contains("sau chữ ký",invalid.Message);
    }
    [Fact]
    public async Task ModifyingSignedBytesIsDetected()
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var bytes=await fixture.Pdf.SignPdfAsync(Source(),fixture.Manager,SignatureType.PersonalSignature,"Manager","First");
        var position=bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("AUDIT_ORIGINAL"));Assert.True(position>=0);bytes[position]=(byte)'X';
        var verification=await fixture.Pdf.VerifyPdfSignaturesAsync(bytes);
        Assert.False(verification.IsValid);Assert.Contains(verification.Signatures,s=>!s.IsIntact);
    }
    [Fact]
    public async Task RevocationRemainsVisibleAfterPrivateKeyIsRemoved()
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var bytes=await fixture.Pdf.SignPdfAsync(Source(),fixture.Manager,SignatureType.PersonalSignature,"Manager","First");
        await fixture.Certificates.RevokeCertificateAsync(fixture.Manager);
        var verification=await fixture.Pdf.VerifyPdfSignaturesAsync(bytes);
        Assert.False(verification.IsValid);Assert.True(verification.Signatures.Single().IsRevoked);
        await Assert.ThrowsAsync<CertificateNotFoundException>(()=>fixture.Pdf.SignPdfAsync(Source(),fixture.Manager,SignatureType.PersonalSignature,"Manager","Again"));
    }
    [Fact]
    public async Task UnknownCaIsNotReportedAsTrusted()
    {
        using var issuer=new Fixture();await issuer.Initialize();
        using var verifier=new Fixture();await verifier.Initialize();
        var bytes=await issuer.Pdf.SignPdfAsync(Source(),issuer.Manager,SignatureType.PersonalSignature,"Manager","First");
        var verification=await verifier.Pdf.VerifyPdfSignaturesAsync(bytes);
        Assert.False(verification.IsValid);Assert.True(verification.Signatures.Single().IsIntact);
        Assert.False(verification.Signatures.Single().IsCertificateTrusted);
    }
    [Fact]
    public async Task ExpiredCertificateCannotProduceAValidVerificationResult()
    {
        var actor=Guid.NewGuid();using var rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=Expired fixture",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3),DateTimeOffset.UtcNow.AddDays(-2));
        var certificates=new Mock<ICertificateService>();
        certificates.Setup(c=>c.GetUserCertificateAsync(actor)).ReturnsAsync(new UserCertificate { UserId=actor,FullName="Expired fixture",CertificatePfx=certificate.Export(X509ContentType.Pkcs12,actor.ToString()) });
        certificates.Setup(c=>c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(true); // Model a stored historical signature with an expired certificate.
        certificates.Setup(c=>c.GetTrust(It.IsAny<byte[]>())).Returns(new CertificateTrust(true,false));
        var pdf=new PdfSigningService(certificates.Object,NullLogger<PdfSigningService>.Instance);
        var signed=await pdf.SignPdfAsync(Source(),actor,SignatureType.PersonalSignature,"Expired fixture","Fixture");
        var verification=await pdf.VerifyPdfSignaturesAsync(signed);
        Assert.False(verification.IsValid);Assert.True(verification.Signatures.Single().IsIntact);Assert.False(verification.Signatures.Single().IsCertificateValid);
    }
    [Fact]
    public void ImagesRequireOcrBoundToTheSameSource()
    {
        Assert.Throws<SignServiceException>(()=>SigningDocumentPreparer.ValidateOcr(null,"documents/a.jpg"));
        var result=JsonNode.Parse("""{"provider":"gemini","sourcePath":"documents/a.jpg","layout":{"blocks":[{"type":"paragraph","text":"Approved content"}]}}""")!.AsObject();
        SigningDocumentPreparer.ValidateOcr(result,"documents/a.jpg");
        Assert.Throws<IncomingConflictException>(()=>SigningDocumentPreparer.ValidateOcr(result,"documents/b.jpg"));
    }
}
