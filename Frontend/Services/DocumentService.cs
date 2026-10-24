using HauDocumentApp.Models;
using System.Net.Http.Json;

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
        if (!string.IsNullOrEmpty(typeId)) q += $"&docTypeId={Uri.EscapeDataString(typeId)}";
        if (!string.IsNullOrEmpty(search)) q += $"&search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<PagedResult<DocumentDto>>(q);
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid id)
        => await _api.GetAsync<DocumentDto>($"api/documents/{id}");

    public Task<PagedResult<DocumentDto>?> GetDeletedDocumentsAsync(int page = 1, string? search = null)
        => _api.GetAsync<PagedResult<DocumentDto>>($"api/documents/deleted?page={page}&pageSize=15&search={Uri.EscapeDataString(search ?? "")}");

    public async Task DeleteDraftAsync(Guid id, string? reason)
    {
        using var response = await _api.DeleteResponseAsync($"api/documents/{id}", new { reason });
        await EnsureDocumentActionAsync(response);
    }

    public async Task CancelDocumentAsync(Guid id, string reason, Guid expectedVersion)
    {
        using var response = await _api.PostResponseAsync($"api/documents/{id}/cancel", new { reason, expectedVersion });
        await EnsureDocumentActionAsync(response);
    }

    public async Task RestoreDraftAsync(Guid id, string reason)
    {
        using var response = await _api.PostResponseAsync($"api/documents/{id}/restore", new { reason });
        await EnsureDocumentActionAsync(response);
    }

    public async Task<bool> PurgeDocumentAsync(Guid id, string reason, string confirmationTitle, Guid expectedVersion)
    {
        using var response = await _api.PostResponseAsync($"api/documents/{id}/purge", new { reason, confirmationTitle, expectedVersion });
        await EnsureDocumentActionAsync(response);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("filesPendingCleanup").GetBoolean();
    }

    public async Task ReceiveDocumentAsync(Guid id)
    {
        using var response = await _api.PostResponseAsync($"api/documents/{id}/receive", new { });
        await EnsureDocumentActionAsync(response);
    }

    public async Task SubmitIncomingAsync(Guid id, Guid expectedVersion, string? comment)
    {
        using var response = await _api.PostResponseAsync($"api/documents/{id}/incoming-submit", new { expectedVersion, comment });
        await EnsureDocumentActionAsync(response);
    }

    public async Task SignIncomingAsync(Guid id, string action, object request)
    {
        using var response = await _api.PostResponseAsync($"api/signatures/incoming/{id}/{action}", request);
        await EnsureDocumentActionAsync(response);
    }

    private static async Task EnsureDocumentActionAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = "Không thực hiện được thao tác. Hãy tải lại công văn.";
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("message", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                message = value.GetString() ?? message;
        }
        catch (System.Text.Json.JsonException) { }
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    public async Task<DocumentDto?> CreateDocumentAsync(CreateDocumentDto dto)
        => await _api.PostAsync<DocumentDto>("api/documents", new
        {
            dto.Title, DocNumber = dto.DocumentNumber, DocTypeId = dto.DocumentTypeId,
            dto.IssuedDate
        });

    public async Task<DocumentDto?> UpdateDocumentAsync(Guid id, UpdateDocumentDto dto)
        => await _api.PutAsync<DocumentDto>($"api/documents/{id}", new
        {
            dto.Title, DocNumber = dto.DocumentNumber, DocTypeId = dto.DocumentTypeId,
            dto.IssuedDate
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

        if (document.MinioObjectName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("Word không cần chạy OCR. Hãy tải PDF hoặc ảnh nếu cần nhận diện.");

        using var response = await _api.PostResponseAsync("api/ocr/process", new
        {
            doc_id = id.ToString(),
            minio_path = document.MinioObjectName,
            token = string.Empty
        });

        if (!response.IsSuccessStatusCode)
        {
            var message = "OCR thất bại. Vui lòng thử lại.";
            try
            {
                using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    && body.RootElement.TryGetProperty("detail", out var detail)
                    && detail.ValueKind == System.Text.Json.JsonValueKind.String)
                    message = detail.GetString() ?? message;
            }
            catch (System.Text.Json.JsonException) { }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return true;
    }

    // GET api/documents/types  (route trong DocumentsController)
    public async Task<List<DocumentTypeDto>?> GetDocumentTypesAsync()
        => await _api.GetAsync<List<DocumentTypeDto>>("api/documents/types");

    public Task<byte[]?> GetFileAsync(Guid id)
        => _api.GetBytesAsync($"api/documents/{id}/file");

    public Task<List<AssigneeDto>?> GetAssigneesAsync(string search, Guid? departmentId = null)
        => _api.GetAsync<List<AssigneeDto>>($"api/users/assignees?search={Uri.EscapeDataString(search)}"
            + (departmentId.HasValue ? $"&departmentId={departmentId}&role=Specialist" : ""));

    public async Task<DocumentDto?> UploadFileAsync(Guid id, System.IO.Stream fileStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            DocumentFileFormats.GetContentType(fileName)
                ?? throw new ArgumentException("Chỉ hỗ trợ PDF, PNG, JPG/JPEG hoặc WEBP.", nameof(fileName)));
        content.Add(fileContent, "file", fileName);
        using var response = await _api.PostFormAsync($"api/documents/{id}/upload", content);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
                ? "File vượt giới hạn tải lên. Dung lượng tối đa là 50 MB."
                : "Không tải được file. Vui lòng thử lại tại trang chi tiết.";
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<DocumentUploadResponse>() is { Success: true } result
            ? result.Data : null;
    }

    private sealed class DocumentUploadResponse
    {
        public bool Success { get; set; }
        public DocumentDto? Data { get; set; }
    }
}
