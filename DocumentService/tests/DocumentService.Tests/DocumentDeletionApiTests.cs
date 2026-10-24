using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentService.API.Controllers;
using DocumentService.API.Middleware;
using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Service = DocumentService.Infrastructure.Services.DocumentService;

namespace DocumentService.Tests;

public class DocumentDeletionApiTests
{
    [Fact]
    public async Task IncomingSubmitRequiresClerkAndClerkCannotUseTheAssignmentApi()
    {
        var actor = Guid.NewGuid(); var version = Guid.NewGuid(); var id = Guid.NewGuid();
        var service = new Mock<IDocumentService>();
        service.Setup(s => s.SubmitIncomingAsync(id, It.Is<DocumentActor>(a => a.UserId == actor && a.Roles.Contains("Clerk")), version, "Fixture"))
            .ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto { Id = id, Status = DocumentStatus.PendingDirection });
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpClient(); builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("Fixture-User", actor.ToString());
        foreach (var role in new[] { "Admin", "Specialist", "Manager", "BoardOfDirectors" })
        {
            client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/incoming-submit", new { ExpectedVersion = version, Comment = "Fixture" })).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", "Clerk");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/incoming-submit", new { ExpectedVersion = version, Comment = "Fixture", UserId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/assign", new { ToUserId = Guid.NewGuid() })).StatusCode);
        service.Verify(s => s.SubmitIncomingAsync(id, It.IsAny<DocumentActor>(), version, "Fixture"), Times.Once);
        service.Verify(s => s.AssignAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    private sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Fixture-User", out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()) };
            claims.AddRange(Request.Headers["Fixture-Roles"].ToString().Split(',').Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    [Fact]
    public async Task HttpRoutesEnforceJwtActorRoleReasonAndDeletedVisibility()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpClient();
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
        builder.Services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
        builder.Services.AddScoped<IDocumentProcessRepository, DocumentProcessRepository>();
        builder.Services.AddScoped<IDocumentService, Service>();
        builder.Services.AddSingleton(Mock.Of<IFileStorageService>()); builder.Services.AddSingleton(Mock.Of<IKafkaProducerService>());
        await using var app = builder.Build(); app.UseExceptionHandling(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        var id = Guid.NewGuid(); var owner = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync(); await db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT NOT NULL)");
            db.Add(new Document { Id = id, Title = "HTTP fixture", CreatedById = owner, CreatedAt = DateTime.UtcNow,
                DeletionEligibilityConfirmed = true, DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002") });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); using var client = app.GetTestClient();
        async Task<HttpResponseMessage> Delete(object? body) => await client.SendAsync(new(HttpMethod.Delete, $"/api/documents/{id}") { Content = body == null ? null : JsonContent.Create(body) });
        void Actor(Guid user, string roles)
        {
            client.DefaultRequestHeaders.Remove("Fixture-User"); client.DefaultRequestHeaders.Remove("Fixture-Roles");
            client.DefaultRequestHeaders.Add("Fixture-User", user.ToString()); client.DefaultRequestHeaders.Add("Fixture-Roles", roles);
        }
        (await Delete(null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Actor(owner, "Manager"); (await Delete(new { reason = "Test" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Actor(Guid.NewGuid(), "Specialist");
        (await Delete(new { userId = owner, createdById = owner, roles = new[] { "Admin" } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Actor(owner, "Specialist,Admin");
        (await Delete(new { reason = " " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Actor(owner, "Specialist"); (await Delete(null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Delete(null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/documents/{id}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/documents/{id}/submit", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/documents/deleted")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/documents/{id}/restore", new { reason = "Test" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Actor(Guid.NewGuid(), "Admin");
        (await client.GetAsync("/api/documents/deleted")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/documents/{id}/restore", new { reason = "Đối chiếu" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
