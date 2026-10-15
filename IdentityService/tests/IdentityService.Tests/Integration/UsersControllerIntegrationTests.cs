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

    [Fact]
    public async Task UserWithoutEmail_ShouldSupportDepartmentAssignmentTransferAndRemoval()
    {
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var initialDepartmentId = new Guid("22222222-0000-0000-0000-000000000001");
        var departmentResponse = await _client.PostAsJsonAsync("/api/departments", new
        {
            DeptName = "Phòng kiểm thử phân công", DeptCode = "TEST" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()
        });
        departmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var department = JsonDocument.Parse(await departmentResponse.Content.ReadAsStringAsync());
        var targetDepartmentId = department.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        var createResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Username = "GV" + Guid.NewGuid().ToString("N")[..18], FullName = "Người dùng chưa có email",
            Password = "TemporaryPass123!", DepartmentId = initialDepartmentId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var data = created.RootElement.GetProperty("data");
        var userId = data.GetProperty("id").GetGuid();
        data.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("departmentId").GetGuid().Should().Be(initialDepartmentId);
        data.GetProperty("departmentName").GetString().Should().Be("Trường Đại học Kiến Trúc Hà Nội");

        var updateResponse = await _client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserDto
        {
            FullName = "Người dùng chưa có email", DepartmentId = targetDepartmentId, IsActive = true
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var updated = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        updated.RootElement.GetProperty("data").GetProperty("departmentId").GetGuid().Should().Be(targetDepartmentId);
        updated.RootElement.GetProperty("data").GetProperty("departmentName").GetString().Should().Be("Phòng kiểm thử phân công");

        var invalidResponse = await _client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserDto
        {
            FullName = "Người dùng chưa có email", DepartmentId = Guid.NewGuid(), IsActive = true
        });
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var unchanged = JsonDocument.Parse(await _client.GetStringAsync($"/api/users/{userId}"));
        unchanged.RootElement.GetProperty("data").GetProperty("departmentId").GetGuid().Should().Be(targetDepartmentId);

        var removeResponse = await _client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserDto
        {
            FullName = "Người dùng chưa có email", DepartmentId = null, IsActive = true
        });
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var removed = JsonDocument.Parse(await _client.GetStringAsync($"/api/users/{userId}"));
        removed.RootElement.GetProperty("data").GetProperty("departmentId").ValueKind.Should().Be(JsonValueKind.Null);
        removed.RootElement.GetProperty("data").GetProperty("departmentName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AdminResetPassword_ShouldRequireAdminAndRestartEmailVerification()
    {
        var username = "GV" + Guid.NewGuid().ToString("N")[..18];
        var email = username.ToLowerInvariant() + "@hau.test";
        var adminToken = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var createResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Username = username, FullName = "Giảng viên kiểm thử", Email = email,
            Password = "OriginalPass123!"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var userId = created.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        _client.DefaultRequestHeaders.Authorization = null;
        var unauthorized = await _client.PostAsJsonAsync($"/api/users/{userId}/reset-password",
            new AdminResetPasswordDto { TemporaryPassword = "TemporaryPass456!", ConfirmPassword = "TemporaryPass456!" });
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var oldLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = username, Password = "OriginalPass123!"
        });
        var oldLogin = await oldLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        oldLogin.Should().NotBeNull();

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var resetResponse = await _client.PostAsJsonAsync($"/api/users/{userId}/reset-password",
            new AdminResetPasswordDto { TemporaryPassword = "TemporaryPass456!", ConfirmPassword = "TemporaryPass456!" });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var userResponse = await _client.GetAsync($"/api/users/{userId}");
        using var updated = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
        updated.RootElement.GetProperty("data").GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);

        var validation = await _client.PostAsJsonAsync("/api/auth/validate-token",
            new ValidateTokenRequestDto { Token = oldLogin!.AccessToken });
        var oldTokenStatus = await validation.Content.ReadFromJsonAsync<ValidateTokenResponseDto>();
        oldTokenStatus!.IsValid.Should().BeFalse();

        _client.DefaultRequestHeaders.Authorization = null;
        var newLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = username, Password = "TemporaryPass456!"
        });
        newLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var newLogin = await newLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        newLogin!.MustChangePassword.Should().BeTrue();

        var emailLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = email, Password = "TemporaryPass456!"
        });
        emailLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
