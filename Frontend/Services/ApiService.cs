using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blazored.LocalStorage;
using HauDocumentApp.Models;
using Microsoft.AspNetCore.Components;

namespace HauDocumentApp.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly NavigationManager _navManager;
    private const string TokenKey = "auth_token";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ApiService(
        HttpClient httpClient,
        ILocalStorageService localStorage,
        NavigationManager navManager)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _navManager = navManager;
    }

    private async Task<HttpClient> GetClientAsync()
    {
        var client = _httpClient;
        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Tự động unwrap ApiResponse&lt;T&gt; nếu backend có bọc wrapper.
    /// Nếu JSON là object có field "data" và "success" → lấy .data
    /// Nếu JSON là trực tiếp T (array hoặc object khác) → deserialize thẳng
    /// </summary>
    private static T? SmartDeserialize<T>(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            // Nếu là object có field "success" → đây là ApiResponse<T>
            if (node is JsonObject obj && obj.ContainsKey("success"))
            {
                var dataNode = obj["data"];
                if (dataNode == null) return default;
                return dataNode.Deserialize<T>(JsonOpts);
            }
            // Không phải wrapper → deserialize trực tiếp
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        var client = await GetClientAsync();
        var resp = await client.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { _navManager.NavigateTo("/login"); return default; }
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync();
        return SmartDeserialize<T>(json);
    }

    public async Task<T?> PostAsync<T>(string url, object? body = null)
    {
        var client = await GetClientAsync();
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            : null;
        var resp = await client.PostAsync(url, content);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { _navManager.NavigateTo("/login"); return default; }
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync();
        return SmartDeserialize<T>(json);
    }

    public async Task<T?> PutAsync<T>(string url, object? body = null)
    {
        var client = await GetClientAsync();
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            : null;
        var resp = await client.PutAsync(url, content);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { _navManager.NavigateTo("/login"); return default; }
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync();
        return SmartDeserialize<T>(json);
    }

    public async Task<T?> PatchAsync<T>(string url, object? body = null)
    {
        var client = await GetClientAsync();
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            : null;
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        var resp = await client.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { _navManager.NavigateTo("/login"); return default; }
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync();
        return SmartDeserialize<T>(json);
    }

    public async Task DeleteAsync(string url)
    {
        var client = await GetClientAsync();
        var resp = await client.DeleteAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { _navManager.NavigateTo("/login"); return; }
        // Không throw — để caller quyết định
    }

    public async Task<HttpResponseMessage> PostFormAsync(string url, MultipartFormDataContent content)
    {
        var client = await GetClientAsync();
        return await client.PostAsync(url, content);
    }
}
