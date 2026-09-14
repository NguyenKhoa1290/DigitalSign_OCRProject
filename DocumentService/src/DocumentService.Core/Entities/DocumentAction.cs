namespace DocumentService.Core.Entities;

public static class DocumentAction
{
    public const string Submit = "Submit";           // Trình duyệt
    public const string DeptSign = "DeptSign";       // Ký nháy cấp phòng
    public const string DirectorSign = "DirectorSign"; // Ký số pháp nhân BGH
    public const string Reject = "Reject";           // Từ chối/Trả lại
    public const string Publish = "Publish";         // Phát hành
    public const string Assign = "Assign";           // Phân công
    public const string UpdateOCR = "UpdateOCR";     // Cập nhật kết quả OCR
}
