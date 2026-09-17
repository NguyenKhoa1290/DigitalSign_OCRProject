using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IdentityService.Core.DTOs.Auth;
using IdentityService.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace IdentityService.Tests.Integration;

/// <summary>
/// Integration tests for AuthController and core endpoints.
/// Uses WebApplicationFactory with InMemory database.
/// </summary>
public class AuthControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task Login_WithAdminCredentials_ShouldReturn200WithTokens()
    {
        var client = CreateClient();
        var request = new LoginRequestDto { Username = "admin", Password = "Admin@123" };
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturn401()
    {
        var client = CreateClient();
        var request = new LoginRequestDto { Username = "admin", Password = "WrongPassword" };
        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyBody_ShouldReturn400()
    {
        var client = CreateClient();
        var request = new { Username = "", Password = "" };
        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateToken_WithValidToken_ShouldReturnIsValidTrue()
    {
        var client = CreateClient();
        // First login
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Then validate
        var validateRequest = new ValidateTokenRequestDto { Token = loginContent!.AccessToken };
        var validateResponse = await client.PostAsJsonAsync("/api/auth/validate-token", validateRequest);

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await validateResponse.Content.ReadFromJsonAsync<ValidateTokenResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToken_WithInvalidToken_ShouldReturnIsValidFalse()
    {
        var client = CreateClient();
        var request = new ValidateTokenRequestDto { Token = "not.a.valid.jwt" };
        var response = await client.PostAsJsonAsync("/api/auth/validate-token", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ValidateTokenResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result!.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_WithValidToken_ShouldReturn200()
    {
        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent!.AccessToken);

        var response = await client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllUsers_WithoutAuth_ShouldReturn401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllUsers_WithAdminToken_ShouldReturn200()
    {
        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent!.AccessToken);

        var response = await client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDepartments_WithAuth_ShouldReturn200WithSeededData()
    {
        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent!.AccessToken);

        var response = await client.GetAsync("/api/departments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRoles_WithAuth_ShouldReturn200()
    {
        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Username = "admin", Password = "Admin@123" });
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent!.AccessToken);

        var response = await client.GetAsync("/api/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthCheck_ShouldReturn200()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// Custom WebApplicationFactory that replaces PostgreSQL with InMemory database for testing.
/// Manually seeds required test data (roles, departments, admin user).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"IntegrationTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace production PostgreSQL DbContext with InMemory DbContext.
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            // Register fresh InMemory DbContext
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Manually seed test data into InMemory database
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed Roles
        var adminRoleId = new Guid("11111111-0000-0000-0000-000000000001");
        if (!db.AppRoles.Any())
        {
            db.AppRoles.AddRange(
                new IdentityService.Core.Entities.AppRole { Id = adminRoleId, RoleName = "Admin", Description = "Administrator" },
                new IdentityService.Core.Entities.AppRole { Id = new Guid("11111111-0000-0000-0000-000000000002"), RoleName = "Clerk" },
                new IdentityService.Core.Entities.AppRole { Id = new Guid("11111111-0000-0000-0000-000000000003"), RoleName = "Specialist" },
                new IdentityService.Core.Entities.AppRole { Id = new Guid("11111111-0000-0000-0000-000000000004"), RoleName = "Manager" },
                new IdentityService.Core.Entities.AppRole { Id = new Guid("11111111-0000-0000-0000-000000000005"), RoleName = "BoardOfDirectors" }
            );
        }

        // Seed Department
        var deptId = new Guid("22222222-0000-0000-0000-000000000001");
        if (!db.Departments.Any())
        {
            db.Departments.Add(new IdentityService.Core.Entities.Department
            {
                Id       = deptId,
                DeptName = "Trường Đại học Kiến Trúc Hà Nội",
                DeptCode = "HAU",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        // Seed Admin user with correct BCrypt hash for "Admin@123"
        var adminId = new Guid("33333333-0000-0000-0000-000000000001");
        if (!db.AppUsers.Any())
        {
            db.AppUsers.Add(new IdentityService.Core.Entities.AppUser
            {
                Id           = adminId,
                Username     = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 4), // low workFactor for tests
                FullName     = "System Administrator",
                Email        = "admin@hau.edu.vn",
                DepartmentId = deptId,
                IsActive     = true,
                CreatedAt    = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            db.AppUserRoles.Add(new IdentityService.Core.Entities.AppUserRole
            {
                UserId = adminId,
                RoleId = adminRoleId
            });
        }

        db.SaveChanges();

        return host;
    }
}
