namespace HauDocumentApp.Models;

public class DepartmentDto
{
    public Guid Id { get; set; }
    public string DeptName { get; set; } = string.Empty;   // khớp backend
    public string? DeptCode { get; set; }                  // khớp backend
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DepartmentDto> Children { get; set; } = new();

    // Alias helpers cho Razor pages (tương thích với các trang cũ)
    public string Name => DeptName;
    public string? Code => DeptCode;
    public bool IsActive => true;       // backend không trả field này, mặc định true
    public int MemberCount { get; set; } // sẽ được populate riêng nếu cần
}

public class CreateDepartmentDto
{
    public string DeptName { get; set; } = string.Empty;   // khớp backend
    public string? DeptCode { get; set; }                  // khớp backend
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }

    // Alias setters cho các trang dùng Name/Code
    public string? Name { set => DeptName = value ?? string.Empty; get => DeptName; }
    public string? Code { set => DeptCode = value; get => DeptCode; }
}
