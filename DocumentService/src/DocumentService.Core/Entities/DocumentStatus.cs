namespace DocumentService.Core.Entities;

public static class DocumentStatus
{
    public const string Draft = "Draft";
    public const string PendingDeptReview = "PendingDeptReview";
    public const string DeptSigned = "DeptSigned";
    public const string PendingDirectorSign = "PendingDirectorSign";
    public const string DirectorSigned = "DirectorSigned";
    public const string Published = "Published";
    public const string Rejected = "Rejected";

    public static string GetDisplayName(string status) => status switch
    {
        Draft => "Bản nháp",
        PendingDeptReview => "Chờ lãnh đạo phòng ký nháy",
        DeptSigned => "Lãnh đạo phòng đã ký",
        PendingDirectorSign => "Chờ Ban Giám hiệu ký số",
        DirectorSigned => "Ban Giám hiệu đã ký số",
        Published => "Đã phát hành",
        Rejected => "Đã bị từ chối",
        _ => status
    };
}
