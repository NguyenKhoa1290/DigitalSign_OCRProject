namespace HauDocumentApp.Models;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool MustChangePassword { get; set; } = false;

    // Helper — lấy role đầu tiên (thường user chỉ có 1 role)
    public string PrimaryRole => Roles.FirstOrDefault() ?? string.Empty;
}
