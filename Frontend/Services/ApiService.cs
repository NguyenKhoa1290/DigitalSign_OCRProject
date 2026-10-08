using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;

namespace HauDocumentApp.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly NavigationManager _navManager;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ApiService(HttpClient httpClient, AuthService authService, NavigationManager navManager)
    {
        _httpClient = httpClient;
        _authService = authService;
        _navManager = navManager;
    }

    private static T? SmartDeserialize<T>(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj && obj.ContainsKey("success"))
            {
                var dataNode = obj["data"];
                return dataNode == null ? default : dataNode.Deserialize<T>(JsonOpts);
            }

            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRefreshAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) return default;
        return SmartDeserialize<T>(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<T?> PostAsync<T>(string url, object? body = null)
    {
        var json = body == null ? null : JsonSerializer.Serialize(body);
        using var response = await SendWithRefreshAsync(
            HttpMethod.Post,
            url,
            json == null ? null : () => JsonContent(json));
        if (!response.IsSuccessStatusCode) return default;
        return SmartDeserialize<T>(await response.Content.ReadAsStringAsync());
    }

    public async Task<T?> PutAsync<T>(string url, object? body = null)
    {
        var json = body == null ? null : JsonSerializer.Serialize(body);
        using var response = await SendWithRefreshAsync(
            HttpMethod.Put,
            url,
            json == null ? null : () => JsonContent(json));
        if (!response.IsSuccessStatusCode) return default;
        return SmartDeserialize<T>(await response.Content.ReadAsStringAsync());
    }

    public async Task<T?> PatchAsync<T>(string url, object? body = null)
    {
        var json = body == null ? null : JsonSerializer.Serialize(body);
        using var response = await SendWithRefreshAsync(
            HttpMethod.Patch,
            url,
            json == null ? null : () => JsonContent(json));
        if (!response.IsSuccessStatusCode) return default;
        return SmartDeserialize<T>(await response.Content.ReadAsStringAsync());
    }

    public async Task DeleteAsync(string url)
    {
        using var response = await SendWithRefreshAsync(HttpMethod.Delete, url);
    }

    public async Task<bool> DeleteWithResultAsync(string url)
    {
        using var response = await SendWithRefreshAsync(HttpMethod.Delete, url);
        return response.IsSuccessStatusCode;
    }

    public async Task<HttpResponseMessage> PostFormAsync(string url, MultipartFormDataContent content)
    {
        var accessToken = await _authService.GetValidAccessTokenAsync();
        var response = await SendOnceAsync(HttpMethod.Post, url, accessToken, content);

        // Stream upload không tự gửi lại. Preflight ở trên đã refresh trước khi upload.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshedToken = await _authService.RefreshAccessTokenAsync(accessToken);
            if (string.IsNullOrEmpty(refreshedToken)) RedirectToLogin();
        }

        return response;
    }

    public async Task<T?> PostFormAsync<T>(string url, MultipartFormDataContent content)
    {
        using var response = await PostFormAsync(url, content);
        if (!response.IsSuccessStatusCode) return default;
        return SmartDeserialize<T>(await response.Content.ReadAsStringAsync());
    }

    public async Task<byte[]?> GetBytesAsync(string url)
    {
        using var response = await SendWithRefreshAsync(HttpMethod.Get, url);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(
        HttpMethod method,
        string url,
        Func<HttpContent?>? contentFactory = null,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await _authService.GetValidAccessTokenAsync();
        var response = await SendOnceAsync(
            method,
            url,
            accessToken,
            contentFactory?.Invoke(),
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var refreshedToken = await _authService.RefreshAccessTokenAsync(accessToken);
        if (string.IsNullOrEmpty(refreshedToken))
        {
            RedirectToLogin();
            return response;
        }

        response.Dispose();
        return await SendOnceAsync(
            method,
            url,
            refreshedToken,
            contentFactory?.Invoke(),
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string url,
        string? accessToken,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private void RedirectToLogin()
    {
        var relativePath = _navManager.ToBaseRelativePath(_navManager.Uri);
        if (!relativePath.StartsWith("login", StringComparison.OrdinalIgnoreCase))
            _navManager.NavigateTo("/login");
    }

    private static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");
}
