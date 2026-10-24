using SignService.Core.DTOs;

namespace SignService.Core.Interfaces;

public interface IWorkflowDirectory
{
    Task<WorkflowUser> GetUserAsync(Guid id);
    Task<IReadOnlyList<WorkflowDepartment>> GetDepartmentsAsync();
}

public interface IIncomingWorkflow
{
    Task<SignResultDto> DirectAsync(Guid id, WorkflowActor actor, IncomingDirectionRequest request);
    Task<SignResultDto> AssignAsync(Guid id, WorkflowActor actor, IncomingAssignmentRequest request);
}
