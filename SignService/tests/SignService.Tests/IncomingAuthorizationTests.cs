using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SignService.API.Controllers;
using SignService.Core.DTOs;
using SignService.Core.Interfaces;

namespace SignService.Tests;

public class IncomingAuthorizationTests
{
    private sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Fixture-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, Request.Headers["Fixture-Role"].ToString()) }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    [Theory]
    [InlineData("Clerk", "direction", 403)]
    [InlineData("Clerk", "assignment", 403)]
    [InlineData("Manager", "direction", 403)]
    [InlineData("BoardOfDirectors", "assignment", 403)]
    [InlineData("Admin", "direction", 403)]
    [InlineData("Specialist", "assignment", 403)]
    [InlineData("BoardOfDirectors", "direction", 200)]
    [InlineData("Manager", "assignment", 200)]
    public async Task HttpRolesAndSignerIdentityComeFromAuthentication(string role, string action, int expected)
    {
        var user = Guid.NewGuid();
        var workflow = new Mock<IIncomingWorkflow>();
        workflow.Setup(w => w.DirectAsync(It.IsAny<Guid>(), It.Is<WorkflowActor>(a => a.Id == user), It.IsAny<IncomingDirectionRequest>())).ReturnsAsync(new SignResultDto());
        workflow.Setup(w => w.AssignAsync(It.IsAny<Guid>(), It.Is<WorkflowActor>(a => a.Id == user), It.IsAny<IncomingAssignmentRequest>())).ReturnsAsync(new SignResultDto());
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(workflow.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(IncomingSignaturesController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Fixture-User", user.ToString()); client.DefaultRequestHeaders.Add("Fixture-Role", role);
        var response = await client.PostAsJsonAsync($"/api/signatures/incoming/{Guid.NewGuid()}/{action}",
            new { ExpectedVersion = Guid.NewGuid(), DepartmentId = Guid.NewGuid(), ToUserId = Guid.NewGuid(), Comment = "Fixture",
                SignerId = Guid.NewGuid(), Roles = new[] { "Admin" } });
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal(expected == 200 ? 1 : 0, workflow.Invocations.Count);
    }
}
