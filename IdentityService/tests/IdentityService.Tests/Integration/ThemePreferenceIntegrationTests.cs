using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Core.DTOs.Auth;
using IdentityService.Core.Entities;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests.Integration;

public class ThemePreferenceIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "ThemeTest123!";
    private async Task<(Guid Id,string Username)> CreateUser(string role)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Id=Guid.NewGuid(), Username="theme-"+Guid.NewGuid().ToString("N"), FullName="Theme account",
            PasswordHash=BCrypt.Net.BCrypt.HashPassword(Password,4), IsActive=true };
        db.AppUsers.Add(user);
        db.AppUserRoles.Add(new AppUserRole { UserId=user.Id, RoleId=await db.AppRoles.Where(r=>r.RoleName==role).Select(r=>r.Id).SingleAsync() });
        await db.SaveChangesAsync(); return (user.Id,user.Username);
    }
    private async Task<LoginResponseDto> Login(HttpClient client,string username)
    {
        using var response=await client.PostAsJsonAsync("/api/auth/login",new{username,password=Password});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var login=(await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.AccessToken);return login;
    }
    private async Task<string?> ReadTheme(HttpClient client)
    {
        using var response=await client.GetAsync("/api/users/me");response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("theme").GetString();
    }
    [Theory]
    [InlineData("Admin")][InlineData("Clerk")][InlineData("Specialist")][InlineData("Manager")][InlineData("BoardOfDirectors")]
    public async Task OwnThemePersistsAcrossClientsAndLoginWithoutChangingOtherUsersOrProfile(string role)
    {
        using var first=factory.CreateClient();var actor=await CreateUser(role);var other=await CreateUser("Specialist");
        var session=await Login(first,actor.Username);Assert.Equal("light",session.Theme);
        using var save=await first.PutAsJsonAsync("/api/users/me/theme",new{theme="dark",userId=other.Id,fullName="Forged",roles=new[]{"Admin"}});
        Assert.Equal(HttpStatusCode.OK,save.StatusCode);Assert.Equal("dark",await ReadTheme(first));
        using var second=factory.CreateClient();var login=await Login(second,actor.Username);
        Assert.Equal("dark",login.Theme);Assert.Equal("dark",await ReadTheme(second));
        using var refresh=await first.PostAsJsonAsync("/api/auth/refresh-token",new{session.AccessToken,session.RefreshToken});
        Assert.Equal(HttpStatusCode.OK,refresh.StatusCode);Assert.Equal("dark",(await refresh.Content.ReadFromJsonAsync<LoginResponseDto>())!.Theme);
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved=await db.AppUsers.AsNoTracking().SingleAsync(u=>u.Id==actor.Id);
            Assert.Equal("Theme account",saved.FullName);Assert.True(saved.IsActive);
            Assert.Equal("light",await db.AppUsers.Where(u=>u.Id==other.Id).Select(u=>u.Theme).SingleAsync());
        }
        using var light=await second.PutAsJsonAsync("/api/users/me/theme",new{theme="light"});light.EnsureSuccessStatusCode();
        using var third=factory.CreateClient();Assert.Equal("light",(await Login(third,actor.Username)).Theme);
    }
    [Theory]
    [InlineData("")][InlineData("auto")][InlineData("DARK")][InlineData(null)]
    public async Task InvalidThemeDoesNotChangeSavedPreference(string? theme)
    {
        using var client=factory.CreateClient();var actor=await CreateUser("Specialist");await Login(client,actor.Username);
        using var response=await client.PutAsJsonAsync("/api/users/me/theme",new{theme});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal("light",await ReadTheme(client));
    }
    [Fact]
    public async Task AnonymousCannotSaveTheme()
    {
        using var client=factory.CreateClient();using var response=await client.PutAsJsonAsync("/api/users/me/theme",new{theme="dark"});
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
}
