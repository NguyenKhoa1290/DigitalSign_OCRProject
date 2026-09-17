using IdentityService.Core.DTOs.Auth;
using IdentityService.Core.DTOs.Users;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IdentityService.Tests.Integration;

/// <summary>
/// Integration tests for UsersController - CRUD operations
/// </summary>
public class UsersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UsersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var content = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return content!.AccessToken;
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_ShouldReturnAdminUser()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/users/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("data").GetProperty("username").GetString().Should().Be("admin");
    }

    [Fact]
    public async Task GetUserById_WithNonExistentId_ShouldReturn404()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateUser_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/users",
            new CreateUserDto { Username = "test", Password = "test", FullName = "Test" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
