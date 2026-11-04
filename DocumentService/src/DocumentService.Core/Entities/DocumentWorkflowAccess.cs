namespace DocumentService.Core.Entities;

public static class DocumentWorkflowAccess
{
    public static bool CanAuthor(string? typeName, Guid? createdBy, DocumentActor actor) =>
        actor.IsAdmin || (typeName == "CongVanDen"
            ? actor.Roles.Contains("Clerk")
            : actor.Roles.Contains("Specialist") && createdBy.HasValue && createdBy == actor.UserId);

    public static bool CanOcr(string? status, DocumentActor actor) =>
        actor.IsAdmin || actor.Roles.Contains("Clerk") && status is "Draft" or "Rejected";
}
