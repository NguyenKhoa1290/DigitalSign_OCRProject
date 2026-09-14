using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using SignService.Core.Entities;
using SignService.Core.Interfaces;

namespace SignService.Infrastructure.Services;

public class CertificateService : ICertificateService
{
    private readonly string _certsDirectory;
    private readonly ILogger<CertificateService> _logger;

    private const string RootCaPassword = "hau-rootca-2024";
    private const string RootCaFileName = "rootca.pfx";
    private const string RootCaCn = "HAU-RootCA";

    public CertificateService(IConfiguration config, ILogger<CertificateService> logger)
    {
        _certsDirectory = config["CertificateSettings:CertsDirectory"] ?? "certs";
        _logger = logger;

        if (!Directory.Exists(_certsDirectory))
            Directory.CreateDirectory(_certsDirectory);
    }

    public async Task InitializeRootCaAsync()
    {
        var rootCaPath = Path.Combine(_certsDirectory, RootCaFileName);
        if (File.Exists(rootCaPath))
        {
            _logger.LogInformation("Root CA đã tồn tại tại: {Path}", rootCaPath);
            return;
        }

        _logger.LogInformation("Đang tạo Root CA mới...");

        await Task.Run(() =>
        {
            var keyPair = GenerateRsaKeyPair(4096);
            var subjectDn = new X509Name($"CN={RootCaCn}, O=HAU, C=VN");

            var certGen = new X509V3CertificateGenerator();
            certGen.SetSerialNumber(GenerateSerial());
            certGen.SetIssuerDN(subjectDn);
            certGen.SetSubjectDN(subjectDn);
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(20));
            certGen.SetPublicKey(keyPair.Public);

            certGen.AddExtension(X509Extensions.BasicConstraints, true,
                new BasicConstraints(true)); // IsCA = true
            certGen.AddExtension(X509Extensions.KeyUsage, true,
                new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
            certGen.AddExtension(X509Extensions.SubjectKeyIdentifier, false,
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public)));

            var signFactory = new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private, new SecureRandom());
            var certificate = certGen.Generate(signFactory);

            SavePfx(rootCaPath, RootCaPassword, certificate, keyPair.Private, RootCaCn);
        });

        _logger.LogInformation("Root CA đã được tạo thành công tại: {Path}", rootCaPath);
    }

    public async Task<UserCertificate> IssueCertificateAsync(Guid userId, string username, string fullName, int validityYears = 2)
    {
        _logger.LogInformation("Đang cấp certificate cho user: {UserId} - {FullName}", userId, fullName);

        return await Task.Run(() =>
        {
            // Load Root CA
            var (rootCert, rootPrivateKey) = LoadRootCa();

            // Tạo key pair cho user
            var userKeyPair = GenerateRsaKeyPair(2048);

            var subjectDn = new X509Name($"CN={fullName}, OU={username}, O=HAU, C=VN");

            var certGen = new X509V3CertificateGenerator();
            certGen.SetSerialNumber(GenerateSerial());
            certGen.SetIssuerDN(rootCert.SubjectDN);
            certGen.SetSubjectDN(subjectDn);
            certGen.SetNotBefore(DateTime.UtcNow.AddHours(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(validityYears));
            certGen.SetPublicKey(userKeyPair.Public);

            certGen.AddExtension(X509Extensions.BasicConstraints, false,
                new BasicConstraints(false));
            certGen.AddExtension(X509Extensions.KeyUsage, true,
                new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.NonRepudiation));
            certGen.AddExtension(X509Extensions.SubjectKeyIdentifier, false,
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(userKeyPair.Public)));
            certGen.AddExtension(X509Extensions.AuthorityKeyIdentifier, false,
                new AuthorityKeyIdentifier(
                    SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(rootCert.GetPublicKey()),
                    new GeneralNames(new GeneralName(rootCert.SubjectDN)),
                    rootCert.SerialNumber));

            var signFactory = new Asn1SignatureFactory("SHA256WithRSA", rootPrivateKey, new SecureRandom());
            var userCert = certGen.Generate(signFactory);

            // Tính thumbprint
            var thumbprint = GetCertThumbprint(userCert);

            // Lưu PFX
            var pfxPath = Path.Combine(_certsDirectory, $"{userId}.pfx");
            var pfxPassword = userId.ToString();
            SavePfx(pfxPath, pfxPassword, userCert, userKeyPair.Private, $"{fullName} ({username})");

            var pfxBytes = File.ReadAllBytes(pfxPath);

            return new UserCertificate
            {
                UserId = userId,
                Username = username,
                FullName = fullName,
                CertificatePfx = pfxBytes,
                CertificateThumbprint = thumbprint,
                NotBefore = userCert.NotBefore.ToUniversalTime(),
                NotAfter = userCert.NotAfter.ToUniversalTime()
            };
        });
    }

    public async Task<UserCertificate?> GetUserCertificateAsync(Guid userId)
    {
        var pfxPath = Path.Combine(_certsDirectory, $"{userId}.pfx");
        if (!File.Exists(pfxPath))
            return null;

        return await Task.Run(() =>
        {
            var pfxBytes = File.ReadAllBytes(pfxPath);
            var password = userId.ToString();

            using var ms = new MemoryStream(pfxBytes);
            var store = new Pkcs12StoreBuilder().Build();
            store.Load(ms, password.ToCharArray());

            string? alias = null;
            foreach (string a in store.Aliases)
            {
                if (store.IsKeyEntry(a))
                {
                    alias = a;
                    break;
                }
            }

            if (alias == null)
                return null;

            var certEntry = store.GetCertificate(alias);
            var cert = certEntry.Certificate;
            var thumbprint = GetCertThumbprint(cert);

            // Extract CN and OU from subject
            var subject = cert.SubjectDN.ToString();
            var fullName = ExtractDnComponent(subject, "CN") ?? string.Empty;
            var username = ExtractDnComponent(subject, "OU") ?? string.Empty;

            return new UserCertificate
            {
                UserId = userId,
                Username = username,
                FullName = fullName,
                CertificatePfx = pfxBytes,
                CertificateThumbprint = thumbprint,
                NotBefore = cert.NotBefore.ToUniversalTime(),
                NotAfter = cert.NotAfter.ToUniversalTime()
            };
        });
    }

    public bool IsCertificateValid(UserCertificate cert)
    {
        var now = DateTime.UtcNow;
        return cert.NotBefore <= now && now <= cert.NotAfter;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static AsymmetricCipherKeyPair GenerateRsaKeyPair(int keySize)
    {
        var gen = new RsaKeyPairGenerator();
        gen.Init(new KeyGenerationParameters(new SecureRandom(), keySize));
        return gen.GenerateKeyPair();
    }

    private static BigInteger GenerateSerial()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        bytes[0] &= 0x7F; // ensure positive
        return new BigInteger(bytes);
    }

    private (X509Certificate cert, AsymmetricKeyParameter privateKey) LoadRootCa()
    {
        var rootCaPath = Path.Combine(_certsDirectory, RootCaFileName);
        if (!File.Exists(rootCaPath))
            throw new InvalidOperationException("Root CA chưa được khởi tạo. Gọi InitializeRootCaAsync() trước.");

        var pfxBytes = File.ReadAllBytes(rootCaPath);
        using var ms = new MemoryStream(pfxBytes);
        var store = new Pkcs12StoreBuilder().Build();
        store.Load(ms, RootCaPassword.ToCharArray());

        string? alias = null;
        foreach (string a in store.Aliases)
        {
            if (store.IsKeyEntry(a))
            {
                alias = a;
                break;
            }
        }

        if (alias == null)
            throw new InvalidOperationException("Root CA PFX không hợp lệ.");

        var cert = store.GetCertificate(alias).Certificate;
        var privateKey = store.GetKey(alias).Key;
        return (cert, privateKey);
    }

    private static void SavePfx(string filePath, string password, X509Certificate certificate,
        AsymmetricKeyParameter privateKey, string friendlyName)
    {
        var store = new Pkcs12StoreBuilder().Build();

        var certEntry = new X509CertificateEntry(certificate);
        store.SetCertificateEntry(friendlyName, certEntry);
        store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(privateKey), new[] { certEntry });

        using var ms = new MemoryStream();
        store.Save(ms, password.ToCharArray(), new SecureRandom());
        File.WriteAllBytes(filePath, ms.ToArray());
    }

    private static string GetCertThumbprint(X509Certificate cert)
    {
        var encoded = cert.GetEncoded();
        var hash = SHA1.HashData(encoded);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ExtractDnComponent(string dn, string key)
    {
        // Simple parser: "CN=value, OU=other"
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }
}
