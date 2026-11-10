namespace DocumentService.Core.Entities;

public record DocumentAccessScope(Guid UserId, Guid? DepartmentId, IReadOnlyCollection<string> Roles)
{
    public bool IsAdmin => Roles.Contains("Admin");
    public bool ReadAll => IsAdmin || Roles.Contains("BoardOfDirectors");
    public bool CanReview(Guid? departmentId) => IsAdmin || Roles.Contains("Manager")
        && DepartmentId.HasValue && departmentId.HasValue && DepartmentId == departmentId;
}
