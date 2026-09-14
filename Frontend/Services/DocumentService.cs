using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class DocumentService
{
    private readonly ApiService _api;

    public DocumentService(ApiService api) => _api = api;

    public async Task<PagedResult<DocumentDto>?> GetDocumentsAsync(
        int page = 1, int pageSize = 15, string? status = null, string? typeId = null, string? search = null)
    {
        var q = $"api/documents?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) q += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(typeId)) q += $"&typeId={Uri.EscapeDataString(typeId)}";
        if (!string.IsNullOrEmpty(search)) q += $"&search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<PagedResult<DocumentDto>>(q);
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid id)
        => await _api.GetAsync<DocumentDto>($"api/documents/{id}");

    public async Task<DocumentDto?> CreateDocumentAsync(CreateDocumentDto dto)
        => await _api.PostAsync<DocumentDto>("api/documents", dto);

    public async Task<DocumentDto?> UpdateDocumentAsync(Guid id, UpdateDocumentDto dto)
        => await _api.PutAsync<DocumentDto>($"api/documents/{id}", dto);

    public async Task<bool> SubmitDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/submit", new { comment }); return true; } catch { return false; } }

    public async Task<bool> DeptSignDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/dept-sign", new { comment }); return true; } catch { return false; } }

    public async Task<bool> DirectorSignDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/director-sign", new { comment }); return true; } catch { return false; } }

    public async Task<bool> RejectDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/reject", new { comment }); return true; } catch { return false; } }

    public async Task<bool> PublishDocumentAsync(Guid id)
    { try { await _api.PostAsync<object>($"api/documents/{id}/publish", null); return true; } catch { return false; } }

    public async Task<bool> AssignDocumentAsync(Guid id, Guid assignedToId, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/assign", new { assignedToId, comment }); return true; } catch { return false; } }

    // RunOcr: không có endpoint riêng trong DocumentService
    // OCR tự động chạy qua Kafka sau khi upload
    public Task<bool> RunOcrAsync(Guid id) => Task.FromResult(true);

    // GET api/documents/types  (route trong DocumentsController)
    public async Task<List<DocumentTypeDto>?> GetDocumentTypesAsync()
        => await _api.GetAsync<List<DocumentTypeDto>>("api/documents/types");

    // Không có endpoint presigned-url trong backend — lấy file qua proxy Document Service
    // Tạm thời trả về null, bảo UI download trực tiếp qua api/documents/{id}
    public Task<string?> GetPresignedUrlAsync(Guid id)
        => Task.FromResult<string?>(null);

    public async Task<DocumentDto?> UploadFileAsync(Guid id, System.IO.Stream fileStream, string fileName)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        var resp = await _api.PostFormAsync($"api/documents/{id}/upload", content);
        if (resp.IsSuccessStatusCode)
            return System.Text.Json.JsonSerializer.Deserialize<DocumentDto>(await resp.Content.ReadAsStringAsync(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return null;
    }
}

public class PresignedUrlResponse { public string Url { get; set; } = string.Empty; }
