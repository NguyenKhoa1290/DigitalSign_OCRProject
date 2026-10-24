using DocumentService.Core.Exceptions;

namespace DocumentService.Core.Entities;

public sealed record DocumentActor(Guid UserId, IReadOnlyCollection<string> Roles)
{
    public bool IsAdmin => Roles.Contains("Admin");
    public void RequireDeleteRole()
    {
        if (UserId == Guid.Empty || !Roles.Any(r => r is "Admin" or "Clerk" or "Specialist"))
            throw new UnauthorizedDocumentAccessException();
    }
}

public static class DocumentDeletionPolicy
{
    public static bool IsEligible(Document document) =>
        document.Status == DocumentStatus.Draft && document.DeletionEligibilityConfirmed
        && document.CreatedById.HasValue && document.CreatedById != Guid.Empty && document.CreatedAt.HasValue
        && document.FirstSubmittedAt == null && document.FirstAssignedAt == null
        && document.OfficiallyReceivedAt == null && document.SigningStartedAt == null
        && !document.Processes.Any(p => p.Action is DocumentAction.Submit or DocumentAction.Assign
            or DocumentAction.DeptSign or DocumentAction.SubmitDirector or DocumentAction.DirectorSign
            or DocumentAction.Reject or DocumentAction.Publish or DocumentAction.Receive or DocumentAction.Cancel);

    public static void Authorize(Document document, DocumentActor actor, bool restore)
    {
        actor.RequireDeleteRole();
        if (restore && !actor.IsAdmin) throw new UnauthorizedDocumentAccessException();
        if (!actor.IsAdmin && document.CreatedById != actor.UserId)
            throw new UnauthorizedDocumentAccessException("Bạn chỉ được xóa bản nháp do mình tạo.");
    }

    public static void RequireEligible(Document document)
    {
        if (!IsEligible(document))
            throw new DocumentConflictException("Hồ sơ không còn đủ điều kiện xóa/khôi phục, hoặc chưa đối soát được lịch sử. Hãy tải lại công văn.");
    }
}
