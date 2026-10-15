namespace HauDocumentApp.Models;

public static class DocumentFileFormats
{
    public const string Accept = ".pdf,.png,.jpg,.jpeg,.webp";
    public static string? GetContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null
    };
}
