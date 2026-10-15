namespace IdentityService.Core.Entities;

public sealed class GeminiApiConfig
{
    public int Id { get; set; } = 1;
    public string ProtectedApiKey { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
