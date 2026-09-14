namespace DocumentService.Core.DTOs;

public class WorkflowActionDto
{
    public string? Comment { get; set; }
    public Guid? ToUserId { get; set; } // để Assign cho user cụ thể
}
