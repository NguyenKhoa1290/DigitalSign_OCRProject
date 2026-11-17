using System.Reflection;
using System.Text.Json;
using IdentityService.API.Controllers;
using IdentityService.Core.Entities;
using IdentityService.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace IdentityService.Tests;

public class LlmSettingsCompatibilityTests
{
    [Fact]
    public void CanonicalAndLegacySettingsRoutesRequireAdmin()
    {
        var type = typeof(GeminiSettingsController);
        var routes = type.GetCustomAttributes<RouteAttribute>().Select(x => x.Template).ToArray();
        Assert.Contains("api/admin/llm-settings", routes);
        Assert.Contains("api/admin/gemini-settings", routes);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        var internalRoutes = typeof(InternalGeminiSettingsController)
            .GetCustomAttributes<RouteAttribute>().Select(x => x.Template);
        Assert.Contains("api/internal/llm-settings", internalRoutes);
        Assert.Contains("api/internal/gemini-settings", internalRoutes);
    }

    [Fact]
    public async Task ExistingProtectedKeyRemainsReadableAfterLabelChange()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var protection = new EphemeralDataProtectionProvider();
        const string key = "fixture-existing-provider-key-1234";
        db.GeminiApiConfigs.Add(new GeminiApiConfig
        {
            Id = 1, ProtectedApiKey = protection.CreateProtector("HAU.GeminiApiKey.v1").Protect(key),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = new GeminiSettingsController(db, protection, Mock.Of<IHttpClientFactory>());
        var response = Assert.IsType<OkObjectResult>(await controller.Get());
        var json = JsonSerializer.SerializeToElement(response.Value);
        Assert.True(json.GetProperty("configured").GetBoolean());
        Assert.EndsWith("1234", json.GetProperty("maskedKey").GetString());
        Assert.DoesNotContain(key, JsonSerializer.Serialize(response.Value));
    }
}
