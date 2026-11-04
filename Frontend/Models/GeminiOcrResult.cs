using System.Text.Json;

namespace HauDocumentApp.Models;

public class GeminiOcrResult
{
    public string Provider { get; set; } = "gemini";
    public int SchemaVersion { get; set; } = 1;
    public string ModelId { get; set; } = "";
    public int[] SelectedPages { get; set; } = Array.Empty<int>();
    public string[] SourceFileNames { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
    public JsonElement Layout { get; set; }
    public OcrEditedDocument? EditedDocument { get; set; }

    public static GeminiOcrResult? Parse(string? raw)
    {
        try
        {
            var result = JsonSerializer.Deserialize<GeminiOcrResult>(raw ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result?.Provider == "gemini" && result.Layout.ValueKind == JsonValueKind.Object
                && result.Layout.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array
                && blocks.GetArrayLength() > 0 ? result : null;
        }
        catch (JsonException) { return null; }
    }
}

public class OcrEditedDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public Guid EditedById { get; set; }
    public string EditedByName { get; set; } = "";
    public string EditorRole { get; set; } = "";
    public DateTimeOffset EditedAt { get; set; }
    public int Revision { get; set; }
}
