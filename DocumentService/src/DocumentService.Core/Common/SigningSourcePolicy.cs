using System.Text.Json.Nodes;
using DocumentService.Core.Exceptions;
namespace DocumentService.Core.Common;
public static class SigningSourcePolicy
{
    public static void RequireReady(string path, string? ocr)
    {
        if (path.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".docx",StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var raw = JsonNode.Parse(ocr ?? "null");
            if (System.Text.RegularExpressions.Regex.IsMatch(path,@"\.(png|jpe?g|webp)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && raw?["provider"]?.ToString() == "gemini" && raw["layout"]?["blocks"] is JsonArray { Count: > 0 }
                && raw["sourcePath"]?.ToString() == path) return;
        }
        catch (System.Text.Json.JsonException) { }
        throw new DocumentServiceException("Ảnh cần có nội dung OCR khớp tệp trước khi trình ký. PDF và Word được dùng trực tiếp.");
    }
}
