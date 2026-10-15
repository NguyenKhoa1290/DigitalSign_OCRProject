using System.Security.Cryptography;
using System.Text.Json;
using IdentityService.Core.Entities;
using IdentityService.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.API.Controllers;

[ApiController]
[Route("api/admin/gemini-settings")]
[Authorize(Roles = "Admin")]
public sealed class GeminiSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClients;

    public GeminiSettingsController(AppDbContext db, IDataProtectionProvider protection, IHttpClientFactory httpClients)
    {
        _db = db;
        _protector = protection.CreateProtector("HAU.GeminiApiKey.v1");
        _httpClients = httpClients;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var config = await _db.GeminiApiConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        if (config is null) return Ok(new { configured = false, maskedKey = (string?)null, updatedAtUtc = (DateTime?)null });
        var key = _protector.Unprotect(config.ProtectedApiKey);
        return Ok(new { configured = true, maskedKey = Mask(key), updatedAtUtc = config.UpdatedAtUtc });
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] GeminiApiKeyRequest request)
    {
        var key = request.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512)
            return BadRequest(new { message = "API key Gemini không hợp lệ." });

        try
        {
            var models = await GetGenerativeGeminiModels(key);
            if (models.Count == 0)
                return BadRequest(new { message = "Khóa hợp lệ nhưng không tìm thấy model Gemini hỗ trợ generateContent." });

            var config = await _db.GeminiApiConfigs.SingleOrDefaultAsync(x => x.Id == 1);
            if (config is null)
            {
                config = new GeminiApiConfig { Id = 1 };
                _db.GeminiApiConfigs.Add(config);
            }
            config.ProtectedApiKey = _protector.Protect(key);
            config.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { configured = true, maskedKey = Mask(key), updatedAtUtc = config.UpdatedAtUtc, models });
        }
        catch (InvalidGeminiKeyException)
        {
            return BadRequest(new { message = "Google AI Studio từ chối API key. Hãy kiểm tra khóa và quyền Gemini API." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Không kết nối được Google AI Studio để kiểm tra khóa." });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { message = "Google AI Studio phản hồi quá thời gian kiểm tra khóa." });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        var config = await _db.GeminiApiConfigs.SingleOrDefaultAsync(x => x.Id == 1);
        if (config is not null)
        {
            _db.GeminiApiConfigs.Remove(config);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    private async Task<List<string>> GetGenerativeGeminiModels(string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1beta/models");
        request.Headers.Add("x-goog-api-key", key);
        using var response = await _httpClients.CreateClient("gemini-key-validation").SendAsync(request);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidGeminiKeyException();
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (!json.RootElement.TryGetProperty("models", out var modelArray)) return [];

        return modelArray.EnumerateArray()
            .Where(item =>
            {
                var name = item.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                var supportsGenerate = item.TryGetProperty("supportedGenerationMethods", out var methods)
                    && methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
                return name?.StartsWith("models/gemini-", StringComparison.OrdinalIgnoreCase) == true
                    && !name.Contains("embedding", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("tts", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("live", StringComparison.OrdinalIgnoreCase)
                    && supportsGenerate;
            })
            .Select(item => item.GetProperty("name").GetString()!["models/".Length..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Mask(string key) => key.Length <= 8 ? "••••••••" : $"••••••••{key[^4..]}";
}

public sealed record GeminiApiKeyRequest(string? ApiKey);

[ApiController]
[Route("api/internal/gemini-settings")]
[AllowAnonymous]
public sealed class InternalGeminiSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;

    public InternalGeminiSettingsController(AppDbContext db, IDataProtectionProvider protection, IConfiguration configuration)
    {
        _db = db;
        _protector = protection.CreateProtector("HAU.GeminiApiKey.v1");
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var expected = _configuration["ServiceAuth:OcrServiceToken"];
        var supplied = Request.Headers["X-OCR-Service-Token"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || supplied.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(expected)))
            return Unauthorized();

        var config = await _db.GeminiApiConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        return config is null
            ? NotFound(new { message = "Gemini API key chưa được cấu hình." })
            : Ok(new { apiKey = _protector.Unprotect(config.ProtectedApiKey) });
    }
}

public sealed class InvalidGeminiKeyException : Exception { }
