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
        => await _api.PostAsync<DocumentDto>("api/documents", new
        {
            dto.Title, DocNumber = dto.DocumentNumber, DocTypeId = dto.DocumentTypeId,
            IssuedDate = dto.IssuedDate.HasValue ? DateOnly.FromDateTime(dto.IssuedDate.Value) : (DateOnly?)null
        });

    public async Task<DocumentDto?> UpdateDocumentAsync(Guid id, UpdateDocumentDto dto)
        => await _api.PutAsync<DocumentDto>($"api/documents/{id}", new
        {
            dto.Title, DocNumber = dto.DocumentNumber, DocTypeId = dto.DocumentTypeId,
            IssuedDate = dto.IssuedDate.HasValue ? DateOnly.FromDateTime(dto.IssuedDate.Value) : (DateOnly?)null
        });

    public async Task<bool> SubmitDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/submit", new { comment }); return true; } catch { return false; } }

    public async Task<bool> DeptSignDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/dept-sign", new { comment }); return true; } catch { return false; } }

    public async Task<bool> SubmitDirectorDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/submit-director", new { comment }); return true; } catch { return false; } }

    public async Task<bool> DirectorSignDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/director-sign", new { comment }); return true; } catch { return false; } }

    public async Task<bool> RejectDocumentAsync(Guid id, string? comment = null)
    { try { await _api.PostAsync<object>($"api/documents/{id}/reject", new { comment }); return true; } catch { return false; } }

    public async Task<bool> PublishDocumentAsync(Guid id)
    { try { await _api.PostAsync<object>($"api/documents/{id}/publish", null); return true; } catch { return false; } }

    public async Task<bool> AssignDocumentAsync(Guid id, Guid assignedToId, string? comment = null)
    { try { return await _api.PostAsync<DocumentDto>($"api/documents/{id}/assign", new { toUserId = assignedToId, comment }) is not null; } catch { return false; } }

    public async Task<bool> RunOcrAsync(Guid id)
    {
        var document = await GetDocumentAsync(id);
        if (document is null || string.IsNullOrWhiteSpace(document.MinioObjectName))
            return false;

        var result = await _api.PostAsync<object>("api/ocr/process", new
        {
            doc_id = id.ToString(),
            minio_path = document.MinioObjectName,
            token = string.Empty
        });

        return result is not null;
    }

    // GET api/documents/types  (route trong DocumentsController)
    public async Task<List<DocumentTypeDto>?> GetDocumentTypesAsync()
        => await _api.GetAsync<List<DocumentTypeDto>>("api/documents/types");

    public Task<byte[]?> GetFileAsync(Guid id)
        => _api.GetBytesAsync($"api/documents/{id}/file");

    public Task<List<AssigneeDto>?> GetAssigneesAsync(string search)
        => _api.GetAsync<List<AssigneeDto>>($"api/users/assignees?search={Uri.EscapeDataString(search)}");

    public async Task<DocumentDto?> UploadFileAsync(Guid id, System.IO.Stream fileStream, string fileName)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        return await _api.PostFormAsync<DocumentDto>($"api/documents/{id}/upload", content);
    }
}
