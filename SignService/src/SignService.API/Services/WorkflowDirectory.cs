using System.Net;
using System.Text.Json;
using SignService.Core.DTOs;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;

namespace SignService.API.Services;

public class WorkflowDirectory(IHttpClientFactory clients, IHttpContextAccessor context) : IWorkflowDirectory
{
    public async Task<WorkflowUser> GetUserAsync(Guid id)
    {
        if (id == Guid.Empty) throw new SignServiceException("Chọn tài khoản hợp lệ.");
        var user = await ReadAsync<WorkflowUser>($"api/users/{id}");
        if (user.Id != id || user.Roles == null || string.IsNullOrWhiteSpace(user.FullName))
            throw new WorkflowDirectoryUnavailableException();
        return user;
    }

    public async Task<IReadOnlyList<WorkflowDepartment>> GetDepartmentsAsync()
        => await ReadAsync<List<WorkflowDepartment>>("api/departments");

    private async Task<T> ReadAsync<T>(string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            var bearer = context.HttpContext?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(bearer)) throw new IncomingAccessException();
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
            using var response = await clients.CreateClient("workflow-directory").SendAsync(request);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new IncomingAccessException();
            if (response.StatusCode == HttpStatusCode.NotFound) throw new SignServiceException("Tài khoản hoặc đơn vị không còn tồn tại.");
            if (!response.IsSuccessStatusCode) throw new WorkflowDirectoryUnavailableException();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data))
                throw new WorkflowDirectoryUnavailableException();
            return data.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new WorkflowDirectoryUnavailableException();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { throw new WorkflowDirectoryUnavailableException(); }
    }
}
