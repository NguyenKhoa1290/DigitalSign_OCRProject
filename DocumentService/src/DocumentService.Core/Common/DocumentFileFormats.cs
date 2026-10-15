namespace DocumentService.Core.Common;

public static class DocumentFileFormats
{
    public static string? GetContentType(string? fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null
    };
}
