using System.Text.Json.Serialization;

namespace HauDocumentApp.Models;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DocNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;
    public string? DocTypeName { get; set; }
    public Guid? DocTypeId { get; set; }
    public DateTime? IssuedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? CreatedById { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? MinioPath { get; set; }
    public string? FileName { get; set; }
    public string? OcrDataRaw { get; set; }
    public List<DocumentProcessDto> Processes { get; set; } = new();
    public List<SignatureDto> Signatures { get; set; } = new();

    [JsonIgnore]
    public string? DocumentNumber
    {
        get => DocNumber;
        set => DocNumber = value;
    }

    [JsonIgnore]
    public string DocumentTypeName
    {
        get => DocTypeName ?? string.Empty;
        set => DocTypeName = value;
    }

    [JsonIgnore]
    public Guid? DocumentTypeId
    {
        get => DocTypeId;
        set => DocTypeId = value;
    }

    [JsonIgnore]
    public string? FileUrl
    {
        get => MinioPath;
        set => MinioPath = value;
    }

    [JsonIgnore]
    public string? OcrText
    {
        get => OcrDataRaw;
        set => OcrDataRaw = value;
    }

    [JsonIgnore]
    public List<DocumentProcessDto> ProcessHistory
    {
        get => Processes;
        set => Processes = value ?? new();
    }

    [JsonIgnore]
    public bool HasOcrData => !string.IsNullOrWhiteSpace(OcrDataRaw);

    [JsonIgnore]
    public string? MinioObjectName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MinioPath)) return null;

            var normalized = MinioPath.Trim().Replace('\\', '/').TrimStart('/');
            const string bucketPrefix = "documents/";
            return normalized.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase)
                ? normalized[bucketPrefix.Length..]
                : normalized;
        }
    }
}

public class DocumentProcessDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? ProcessedByName { get; set; }
    public DateTime Timestamp { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTime ProcessedAt
    {
        get => Timestamp;
        set => Timestamp = value;
    }
}
