using System.Security.Claims;
using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.API.Filters;

// Establish access before running any public document/file/OCR action.
public sealed class DocumentScopeFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true) { await next(); return; } // Existing service-token endpoints validate their own credentials.
        var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(idText, out var actor)) throw new UnauthorizedDocumentAccessException();
        var member = await db.Database.SqlQueryRaw<Membership>("""
            SELECT u."Id", u."IsActive", d."Id" AS "DepartmentId", d."DeptName" AS "DepartmentName",
                CASE WHEN d."Id" IS NOT NULL AND d."ParentId" IS NULL THEN TRUE ELSE FALSE END AS "IsSchoolWide"
            FROM "AppUsers" u LEFT JOIN "Departments" d ON d."Id"=u."DepartmentId"
            """).SingleOrDefaultAsync(u => u.Id == actor, context.HttpContext.RequestAborted);
        if (member is not { IsActive: true }) throw new UnauthorizedDocumentAccessException();
        var roles = principal.FindAll(ClaimTypes.Role).Concat(principal.FindAll("role")).Select(c => c.Value).Distinct().ToArray();
        db.AccessScope = new(actor, member.DepartmentId, roles);
        if (context.ActionArguments.Values.OfType<CreateDocumentDto>().SingleOrDefault() is { } create)
        {
            if (!member.DepartmentId.HasValue && !db.AccessScope.IsAdmin)
                throw new DocumentServiceException("Tài khoản chưa thuộc đơn vị nào. Hãy liên hệ quản trị viên trước khi tạo công văn.");
            create.DepartmentId = member.DepartmentId;
            create.DepartmentName = member.DepartmentName;
            create.IsSchoolWide = member.IsSchoolWide;
        }
        context.RouteData.Values.TryGetValue("id", out var docId);
        if (context.RouteData.Values.TryGetValue("documentId", out var documentId)) docId = documentId;
        if (Guid.TryParse(docId?.ToString(), out var id) && !db.AccessScope.IsAdmin)
        {
            var document = await db.VisibleDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id)
                ?? throw new DocumentNotFoundException(id);
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
            if (action is "DeptSign" or "SubmitDirector" && !db.AccessScope.CanReview(document.DepartmentId))
                throw new UnauthorizedDocumentAccessException();
            if (action == "DirectorSign" && !roles.Contains("BoardOfDirectors")) throw new UnauthorizedDocumentAccessException();
            if (action == "Reject" && !(db.AccessScope.CanReview(document.DepartmentId)
                && document.Status is DocumentStatus.PendingDeptReview or DocumentStatus.DeptSigned
                || roles.Contains("BoardOfDirectors") && document.Status == DocumentStatus.PendingDirectorSign))
                throw new UnauthorizedDocumentAccessException();
        }
        if (Guid.TryParse(docId?.ToString(), out id))
        {
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
            var signedPath = await db.Documents.Where(d => d.Id == id).Select(d => d.SignedPdfPath).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (action is "Update" or "UploadFile" or "Upload" or "UpdateOcr"
                && (signedPath != null || await db.SignatureEvidence.AnyAsync(s => s.DocId == id, context.HttpContext.RequestAborted)))
                throw new DocumentConflictException("Công văn đã ký được khóa nội dung, không sửa hoặc thay tệp.");
            if (action is "DeptSign" or "SubmitDirector" && (signedPath == null || !await db.SignatureEvidence.AnyAsync(s => s.DocId == id && s.SignatureType == "PersonalSignature" && s.SignedFilePath != null
                && (db.AccessScope.IsAdmin || s.SignerId == actor), context.HttpContext.RequestAborted)))
                throw new DocumentConflictException("Hãy ký số trên tệp PDF trước khi hoàn tất bước ký nháy.");
            if (action is "DirectorSign" or "Publish" && (signedPath == null || !await db.SignatureEvidence.AnyAsync(s => s.DocId == id && s.SignatureType == "LegalSeal" && s.SignedFilePath == signedPath
                && (db.AccessScope.IsAdmin || action == "Publish" || s.SignerId == actor), context.HttpContext.RequestAborted)))
                throw new DocumentConflictException("Hãy ký số pháp nhân trên tệp PDF trước khi hoàn tất.");
        }
        await next();
    }

    public sealed class Membership
    {
        public Guid Id { get; set; }
        public bool IsActive { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public bool IsSchoolWide { get; set; }
    }
}
