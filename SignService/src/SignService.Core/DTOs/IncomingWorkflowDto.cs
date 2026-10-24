using System.ComponentModel.DataAnnotations;

namespace SignService.Core.DTOs;

public class IncomingDirectionRequest
{
    public Guid ExpectedVersion { get; set; }
    public Guid DepartmentId { get; set; }
    [Required, StringLength(1000)] public string Comment { get; set; } = "";
}

public class IncomingAssignmentRequest
{
    public Guid ExpectedVersion { get; set; }
    public Guid ToUserId { get; set; }
    [Required, StringLength(1000)] public string Comment { get; set; } = "";
}

public record WorkflowActor(Guid Id, IReadOnlyCollection<string> Roles);
public record WorkflowUser(Guid Id, string FullName, Guid? DepartmentId, bool IsActive, List<string> Roles);
public record WorkflowDepartment(Guid Id, string DeptName, Guid? ParentId);
