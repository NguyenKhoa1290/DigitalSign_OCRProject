using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Tests;

public class OcrAuthorizationTests
{
    [Theory]
    [InlineData("Admin", 204)]
    [InlineData("Clerk", 204)]
    [InlineData("Specialist", 403)]
    [InlineData("Manager", 403)]
    [InlineData("BoardOfDirectors", 403)]
    [InlineData(null, 401)]
    public async Task OcrRoutes_EnforceOperatorPolicyBeforeProxy(string? role, int expectedStatus)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();
        builder.Services.AddAuthentication("Fixture")
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthHandler>("Fixture", _ => { });
        builder.Services.AddAuthorization(OcrAccessPolicy.Configure);
        builder.Services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        // Real YARP endpoint metadata and authorization, but never forward to OCR/cloud.
        app.Use(next => context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        app.MapReverseProxy();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        if (role is not null) client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/ocr/process"),
            (HttpMethod.Post, "/api/ocr/process-upload"),
            (HttpMethod.Post, "/api/ocr/export-word-upload"),
            (HttpMethod.Get, "/api/ocr/gemini/models"),
            (HttpMethod.Post, "/api/ocr/gemini/export-word-upload")
        })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        }
    }

    private sealed class FixtureAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public FixtureAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].ToString();
            if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
