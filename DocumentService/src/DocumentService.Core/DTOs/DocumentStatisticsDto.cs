namespace DocumentService.Core.DTOs;

public class DocumentStatisticsDto
{
    public int TotalDocuments { get; set; }
    public int TodayDocuments { get; set; }
    public int PendingDocuments { get; set; }
    public int PublishedDocuments { get; set; }
    public int PendingOcrDocuments { get; set; }
    public int MyDraftDocuments { get; set; }
    public int MyPendingDocuments { get; set; }
    public int PendingDeptDocuments { get; set; }
    public int PendingDirectorDocuments { get; set; }
    public int DirectorSignedDocuments { get; set; }
    public int AssignedDocuments { get; set; }
}
