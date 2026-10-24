using System.Text;
using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using SignService.Core.DTOs;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Services;
using DocContext = DocumentService.Infrastructure.Data.AppDbContext;
using SignContext = SignService.Infrastructure.Data.AppDbContext;

namespace SignService.Tests;

public class IncomingWorkflowTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Guid Id = Guid.NewGuid(), Board = Guid.NewGuid(), Manager = Guid.NewGuid(), Clerk = Guid.NewGuid(), Specialist = Guid.NewGuid();
        public readonly Guid Department = Guid.NewGuid(), Child = Guid.NewGuid();
        public readonly SqliteConnection Connection = new("Data Source=:memory:");
        public readonly Mock<IWorkflowDirectory> Directory = new();
        public readonly Mock<IMinioService> Minio = new();
        public readonly Mock<IPdfSigningService> Pdf = new();
        public readonly Mock<ICertificateService> Certificates = new();
        public readonly Dictionary<string, byte[]> Objects = new() { ["original.pdf"] = Encoding.UTF8.GetBytes("source") };
        public readonly Dictionary<Guid, WorkflowUser> Users = new();
        public SignContext SignDb { get; private set; } = null!;
        public IncomingWorkflow Service => new(SignDb, Directory.Object, Minio.Object, Pdf.Object, Certificates.Object);
        public DocContext DocDb() => new(new DbContextOptionsBuilder<DocContext>().UseSqlite(Connection).Options);
        public async Task Initialize()
        {
            await Connection.OpenAsync();
            await using var db = DocDb();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "Signatures" ("Id" TEXT PRIMARY KEY, "DocId" TEXT NOT NULL, "SignerId" TEXT NOT NULL,
                 "SignatureType" TEXT NOT NULL, "SignedAt" TEXT NOT NULL)
                """);
            db.Add(new Document { Id = Id, Title = "Incoming fixture", CreatedById = Clerk, CreatedAt = DateTime.UtcNow,
                MinioPath = "original.pdf", DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                ConcurrencyVersion = Guid.NewGuid(), DeletionEligibilityConfirmed = true });
            await db.SaveChangesAsync();
            SignDb = new(new DbContextOptionsBuilder<SignContext>().UseSqlite(Connection).Options);
            Users[Board] = new(Board, "Board", null, true, ["BoardOfDirectors"]);
            Users[Manager] = new(Manager, "Manager", Department, true, ["Manager"]);
            Users[Specialist] = new(Specialist, "Specialist", Child, true, ["Specialist"]);
            Directory.Setup(d => d.GetUserAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => Users[id]);
            Directory.Setup(d => d.GetDepartmentsAsync()).ReturnsAsync(new List<WorkflowDepartment> {
                new(Department, "Khoa fixture", null), new(Child, "Bộ môn fixture", Department) });
            Certificates.Setup(c => c.GetUserCertificateAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => new UserCertificate { UserId = id });
            Certificates.Setup(c => c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(true);
            Minio.Setup(m => m.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync((string key) => Objects[key]);
            Minio.Setup(m => m.UploadFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "application/pdf"))
                .Callback<string, byte[], string>((key, bytes, _) => Objects.Add(key, bytes)).Returns(Task.CompletedTask);
            Pdf.Setup(p => p.SignPdfAsync(It.IsAny<byte[]>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((byte[] bytes, Guid _, string type, string name, string reason) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + "|" + type));
            Pdf.Setup(p => p.VerifyPdfSignaturesAsync(It.IsAny<byte[]>())).ReturnsAsync((byte[] bytes) =>
            {
                var text = Encoding.UTF8.GetString(bytes);
                var types = new[] { SignatureType.IncomingDirection, SignatureType.IncomingAssignment }.Where(text.Contains).ToArray();
                return new SignVerifyResult { IsValid = types.Length > 0, SignatureCount = types.Length,
                    Signatures = types.Select(t => new SignatureInfo { SignatureType = t, IsIntact = true }).ToList() };
            });
        }
        public async Task<Document> Current()
        {
            await using var db = DocDb();
            return (await new DocumentRepository(db).GetByIdAsync(Id))!;
        }
        public async Task Submit()
        {
            var document = await Current();
            await using var db = DocDb();
            await new DocumentRepository(db).SubmitIncomingAsync(Id, Clerk, document.ConcurrencyVersion, "Trình chỉ đạo");
        }
        public async Task Direct()
        {
            await Service.DirectAsync(Id, new(Board, ["BoardOfDirectors"]), new() {
                ExpectedVersion = (await Current()).ConcurrencyVersion, DepartmentId = Department, Comment = "Triển khai nhiệm vụ" });
            SignDb.ChangeTracker.Clear();
        }
        public async ValueTask DisposeAsync() { await SignDb.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task IncomingRunsBoardThenManagerWithTwoSignaturesAndPreservesEveryFileVersion()
    {
        await using var f = new Fixture(); await f.Initialize();
        await f.Submit();
        var submitted = await f.Current();
        Assert.Equal(DocumentStatus.PendingDirection, submitted.Status);
        Assert.NotNull(submitted.OfficiallyReceivedAt);
        Assert.NotNull(submitted.FirstSubmittedAt);
        await f.Direct();
        var directed = await f.Current();
        Assert.Equal(DocumentStatus.PendingAssignment, directed.Status);
        Assert.Equal(f.Department, directed.ProcessingDepartmentId);
        Assert.Equal("original.pdf", directed.IncomingOriginalPath);
        Assert.Equal(directed.MinioPath, directed.IncomingDirectionPath);
        Assert.NotEqual(submitted.ConcurrencyVersion, directed.ConcurrencyVersion);
        await f.Service.AssignAsync(f.Id, new(f.Manager, ["Manager"]), new() {
            ExpectedVersion = directed.ConcurrencyVersion, ToUserId = f.Specialist, Comment = "Báo cáo kết quả" });
        var assigned = await f.Current();
        Assert.Equal(DocumentStatus.IncomingAssigned, assigned.Status);
        Assert.Equal(f.Specialist, assigned.AssignedToId);
        Assert.NotNull(assigned.FirstAssignedAt);
        Assert.NotEqual(directed.MinioPath, assigned.MinioPath);
        Assert.Equal(3, f.Objects.Count);
        Assert.Equal("source", Encoding.UTF8.GetString(f.Objects["original.pdf"]));
        Assert.Equal(2, await f.SignDb.Signatures.CountAsync());
        await using var db = f.DocDb();
        Assert.Equal(new[] { "Receive", "SubmitIncoming", "DirectIncoming", "Assign" },
            await db.DocumentProcesses.OrderBy(p => p.Timestamp).Select(p => p.Action).ToArrayAsync());
    }

    [Theory]
    [InlineData("Clerk")]
    [InlineData("Specialist")]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task OnlyBoardCanDirectIncoming(string role)
    {
        await using var f = new Fixture(); await f.Initialize(); await f.Submit();
        await Assert.ThrowsAsync<IncomingAccessException>(() => f.Service.DirectAsync(f.Id, new(f.Board, [role]), new()));
        Assert.Single(f.Objects);
        Assert.Equal(0, await f.SignDb.Signatures.CountAsync());
    }

    [Fact]
    public async Task StaleRequestAndWrongStepCannotSignOrChangeTheDocument()
    {
        await using var f = new Fixture(); await f.Initialize(); await f.Submit();
        await Assert.ThrowsAsync<IncomingConflictException>(() => f.Service.DirectAsync(f.Id, new(f.Board, ["BoardOfDirectors"]),
            new() { DepartmentId = f.Department, Comment = "Chỉ đạo", ExpectedVersion = Guid.NewGuid() }));
        f.SignDb.ChangeTracker.Clear();
        var assignmentVersion = (await f.Current()).ConcurrencyVersion;
        await Assert.ThrowsAsync<IncomingConflictException>(() => f.Service.AssignAsync(f.Id, new(f.Manager, ["Manager"]),
            new() { ToUserId = f.Specialist, Comment = "Phân công", ExpectedVersion = assignmentVersion }));
        Assert.Equal(DocumentStatus.PendingDirection, (await f.Current()).Status);
        Assert.Single(f.Objects);
    }

    [Theory]
    [InlineData("certificate")]
    [InlineData("expired")]
    [InlineData("sign")]
    [InlineData("verify")]
    [InlineData("upload")]
    public async Task SigningFailuresNeverAdvanceStatusOrInsertSignatureHistory(string failure)
    {
        await using var f = new Fixture(); await f.Initialize(); await f.Submit();
        var before = await f.Current();
        if (failure == "certificate") f.Certificates.Setup(c => c.GetUserCertificateAsync(f.Board)).ReturnsAsync((UserCertificate?)null);
        if (failure == "expired") f.Certificates.Setup(c => c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(false);
        if (failure == "sign") f.Pdf.Setup(p => p.SignPdfAsync(It.IsAny<byte[]>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new PdfSigningException("fixture"));
        if (failure == "verify") f.Pdf.Setup(p => p.VerifyPdfSignaturesAsync(It.IsAny<byte[]>())).ReturnsAsync(new SignVerifyResult());
        if (failure == "upload") f.Minio.Setup(m => m.UploadFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "application/pdf")).ThrowsAsync(new IOException("fixture"));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Direct());
        var after = await f.Current();
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.ConcurrencyVersion, after.ConcurrencyVersion);
        Assert.Equal(before.MinioPath, after.MinioPath);
        Assert.Null(after.ProcessingDepartmentId);
        Assert.Equal(0, await f.SignDb.Signatures.CountAsync());
        await using var db = f.DocDb();
        Assert.Equal(2, await db.DocumentProcesses.CountAsync());
    }

    [Theory]
    [InlineData("wrong-manager")]
    [InlineData("wrong-recipient-unit")]
    [InlineData("inactive")]
    [InlineData("not-specialist")]
    [InlineData("revoked-role")]
    public async Task AssignmentEnforcesCurrentAccountRoleAndDepartmentBoundary(string failure)
    {
        await using var f = new Fixture(); await f.Initialize(); await f.Submit(); await f.Direct();
        if (failure == "wrong-manager") f.Users[f.Manager] = f.Users[f.Manager] with { DepartmentId = Guid.NewGuid() };
        if (failure == "wrong-recipient-unit") f.Users[f.Specialist] = f.Users[f.Specialist] with { DepartmentId = Guid.NewGuid() };
        if (failure == "inactive") f.Users[f.Specialist] = f.Users[f.Specialist] with { IsActive = false };
        if (failure == "not-specialist") f.Users[f.Specialist] = f.Users[f.Specialist] with { Roles = ["Clerk"] };
        if (failure == "revoked-role") f.Users[f.Manager] = f.Users[f.Manager] with { Roles = ["Specialist"] };
        var assignmentVersion = (await f.Current()).ConcurrencyVersion;
        await Assert.ThrowsAnyAsync<SignServiceException>(() => f.Service.AssignAsync(f.Id, new(f.Manager, ["Manager"]), new() {
            ExpectedVersion = assignmentVersion, ToUserId = f.Specialist, Comment = "Phân công" }));
        Assert.Equal(DocumentStatus.PendingAssignment, (await f.Current()).Status);
        Assert.Equal(1, await f.SignDb.Signatures.CountAsync());
        Assert.Equal(2, f.Objects.Count);
    }

    [Fact]
    public async Task ModifiedDirectionPdfCannotBeAssigned()
    {
        await using var f = new Fixture(); await f.Initialize(); await f.Submit(); await f.Direct();
        var directed = await f.Current();
        f.Objects[directed.MinioPath] = Encoding.UTF8.GetBytes("tampered");
        await Assert.ThrowsAsync<IncomingConflictException>(() => f.Service.AssignAsync(f.Id, new(f.Manager, ["Manager"]), new() {
            ExpectedVersion = directed.ConcurrencyVersion, ToUserId = f.Specialist, Comment = "Phân công" }));
        Assert.Equal(DocumentStatus.PendingAssignment, (await f.Current()).Status);
        Assert.Equal(1, await f.SignDb.Signatures.CountAsync());
    }
}
