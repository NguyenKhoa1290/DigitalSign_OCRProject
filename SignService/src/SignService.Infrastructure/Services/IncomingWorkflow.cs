using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SignService.Core.DTOs;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

namespace SignService.Infrastructure.Services;

// Incoming signatures run BGH -> department manager, independently of the outgoing personal -> legal sequence.
public class IncomingWorkflow(AppDbContext db, IWorkflowDirectory directory, IMinioService minio,
    IPdfSigningService pdf, ICertificateService certificates) : IIncomingWorkflow
{
    public async Task<SignResultDto> DirectAsync(Guid id, WorkflowActor actor, IncomingDirectionRequest request)
    {
        var user = await AuthorizeAsync(actor, "BoardOfDirectors");
        RequireComment(request.Comment);
        var departments = await directory.GetDepartmentsAsync();
        var department = departments.SingleOrDefault(d => d.Id == request.DepartmentId)
            ?? throw new SignServiceException("Chọn đơn vị xử lý hợp lệ.");
        return await SignAsync(id, actor, user.FullName, request.ExpectedVersion, "PendingDirection",
            SignatureType.IncomingDirection, request.Comment.Trim(), null, document =>
            {
                document.ProcessingDepartmentId = department.Id;
                document.ProcessingDepartmentName = department.DeptName;
                document.Status = "PendingAssignment";
            });
    }

    public async Task<SignResultDto> AssignAsync(Guid id, WorkflowActor actor, IncomingAssignmentRequest request)
    {
        var user = await AuthorizeAsync(actor, "Manager");
        RequireComment(request.Comment);
        var recipient = await directory.GetUserAsync(request.ToUserId);
        if (!recipient.IsActive || !recipient.Roles.Contains("Specialist"))
            throw new SignServiceException("Người nhận phải là chuyên viên đang hoạt động.");
        var departments = await directory.GetDepartmentsAsync();
        return await SignAsync(id, actor, user.FullName, request.ExpectedVersion, "PendingAssignment",
            SignatureType.IncomingAssignment, request.Comment.Trim(), recipient.Id, document =>
            {
                if (!document.ProcessingDepartmentId.HasValue || user.DepartmentId != document.ProcessingDepartmentId)
                    throw new IncomingAccessException();
                var branch = new HashSet<Guid> { document.ProcessingDepartmentId.Value };
                bool added;
                do
                {
                    added = false;
                    foreach (var department in departments)
                        if (department.ParentId.HasValue && branch.Contains(department.ParentId.Value)) added |= branch.Add(department.Id);
                } while (added);
                if (!recipient.DepartmentId.HasValue || !branch.Contains(recipient.DepartmentId.Value))
                    throw new SignServiceException("Chuyên viên phải thuộc đơn vị được chỉ đạo hoặc đơn vị trực thuộc.");
                document.AssignedToId = recipient.Id;
                document.AssignedToName = recipient.FullName;
                document.FirstAssignedAt ??= DateTime.UtcNow;
                document.Status = "IncomingAssigned";
            });
    }

    private async Task<WorkflowUser> AuthorizeAsync(WorkflowActor actor, string role)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Contains(role)) throw new IncomingAccessException();
        var user = await directory.GetUserAsync(actor.Id);
        if (!user.IsActive || !user.Roles.Contains(role)) throw new IncomingAccessException();
        return user;
    }

    private static void RequireComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment) || comment.Length > 1000)
            throw new SignServiceException("Nhập ý kiến chỉ đạo/phân công, tối đa 1000 ký tự.");
    }

    private async Task<SignResultDto> SignAsync(Guid id, WorkflowActor actor, string name, Guid version, string requiredStatus,
        string purpose, string comment, Guid? recipientId, Action<DocumentFileRecord> transition)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\"={id} FOR UPDATE");
        var document = await db.DocumentFiles.SingleOrDefaultAsync(d => d.Id == id) ?? throw new DocumentNotFoundException(id);
        if (!await db.DocumentTypes.AnyAsync(t => t.Id == document.DocTypeId && t.TypeName == "CongVanDen"))
            throw new IncomingConflictException("Thao tác chỉ dành cho công văn đến.");
        if (document.Status != requiredStatus || version == Guid.Empty || document.ConcurrencyVersion != version)
            throw new IncomingConflictException("Công văn đã thay đổi hoặc chưa tới bước xử lý này. Hãy tải lại trang.");
        if (!document.MinioPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new SignServiceException("Cần bản PDF để ký số công văn đến.");
        if (await db.Signatures.AnyAsync(s => s.DocId == id && s.SignatureType == purpose))
            throw new AlreadySignedException(purpose);
        var isAssignment = purpose == SignatureType.IncomingAssignment;
        if (isAssignment && !await db.Signatures.AnyAsync(s => s.DocId == id && s.SignatureType == SignatureType.IncomingDirection))
            throw new IncomingConflictException("Công văn chưa có bằng chứng Ban Giám hiệu ký chỉ đạo.");
        transition(document); // Includes unit/recipient authorization; no external writes before these checks.
        var certificate = await certificates.GetUserCertificateAsync(actor.Id) ?? throw new CertificateNotFoundException(actor.Id);
        if (!certificates.IsCertificateValid(certificate)) throw new CertificateExpiredException();
        var originalPath = document.MinioPath;
        var objectName = originalPath.StartsWith("documents/", StringComparison.Ordinal) ? originalPath[10..] : originalPath;
        var bytes = await minio.DownloadFileAsync(objectName);
        if (isAssignment)
        {
            var prior = await pdf.VerifyPdfSignaturesAsync(bytes);
            if (!prior.IsValid || !prior.Signatures.Any(s => s.SignatureType == SignatureType.IncomingDirection && s.IsIntact))
                throw new IncomingConflictException("Chữ ký chỉ đạo trên PDF không hợp lệ. Chưa ký phân công.");
        }
        // Sign the actual instruction and destination in the cryptographic signature's Reason.
        var signed = await pdf.SignPdfAsync(bytes, actor.Id, purpose, name,
            isAssignment ? $"Phân công cho {document.AssignedToName} ({recipientId}): {comment}"
                         : $"Chỉ đạo đơn vị {document.ProcessingDepartmentName} ({document.ProcessingDepartmentId}): {comment}");
        var verified = await pdf.VerifyPdfSignaturesAsync(signed);
        if (!verified.IsValid || !verified.Signatures.Any(s => s.SignatureType == purpose && s.IsIntact)
            || (isAssignment && !verified.Signatures.Any(s => s.SignatureType == SignatureType.IncomingDirection && s.IsIntact)))
            throw new PdfSigningException("Không xác minh được PDF sau khi ký.");
        var newObject = $"{id}/incoming-{Guid.NewGuid():N}.pdf";
        await minio.UploadFileAsync(newObject, signed, "application/pdf");
        // Keep the original and previous signed objects. A failed DB commit cannot damage either version.
        document.IncomingOriginalPath ??= originalPath;
        if (!isAssignment) document.IncomingDirectionPath = newObject;
        document.MinioPath = newObject;
        document.SigningStartedAt ??= DateTime.UtcNow;
        document.ConcurrencyVersion = Guid.NewGuid();
        var signature = new Signature { Id = Guid.NewGuid(), DocId = id, SignerId = actor.Id, SignatureType = purpose, SignedAt = DateTime.UtcNow };
        db.Signatures.Add(signature);
        // The process trigger reads Documents. Flush the new route before inserting its event;
        // both flushes remain inside this transaction and are rolled back together on failure.
        await db.SaveChangesAsync();
        db.IncomingProcesses.Add(new IncomingProcessRecord { Id = Guid.NewGuid(), DocId = id, FromUserId = actor.Id,
            ToUserId = recipientId, Action = isAssignment ? "Assign" : "DirectIncoming",
            Comment = isAssignment ? $"Ký phân công cho {document.AssignedToName}: {comment}"
                : $"Ký chỉ đạo đơn vị {document.ProcessingDepartmentName}: {comment}", Timestamp = signature.SignedAt });
        if (db.Database.IsNpgsql())
        {
            var trace = Activity.Current?.TraceId.ToString() ?? "incoming:" + signature.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('hau.trace_id', {trace}, true)");
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new SignResultDto { SignatureId = signature.Id, DocId = id, SignerId = actor.Id,
            SignatureType = purpose, SignedAt = signature.SignedAt, Message = "Đã ký và lưu bước xử lý công văn đến." };
    }
}
