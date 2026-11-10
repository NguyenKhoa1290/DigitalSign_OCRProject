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

public partial class DocumentDeletionApiTests
{
    [Theory]
    [InlineData("Admin", true, 201)]
    [InlineData("Admin", false, 201)]
    [InlineData("Clerk", true, 201)]
    [InlineData("Clerk", false, 403)]
    [InlineData("Specialist", true, 403)]
    [InlineData("Specialist", false, 201)]
    [InlineData("Manager", true, 403)]
    [InlineData("Manager", false, 403)]
    [InlineData("BoardOfDirectors", true, 403)]
    [InlineData("BoardOfDirectors", false, 403)]
    [InlineData("", false, 401)]
    public async Task CreateDocumentEnforcesRoleAndDirection(string role, bool incoming, int expected)
    {
        var actor = Guid.NewGuid(); var type = Guid.NewGuid(); var id = Guid.NewGuid();
        var service = new Mock<IDocumentService>();
        service.Setup(s => s.GetDocumentTypesAsync()).ReturnsAsync(new[] { new DocumentService.Core.DTOs.DocumentTypeDto { Id = type, TypeName = incoming ? "CongVanDen" : "CongVanDi" } });
        service.Setup(s => s.CreateDocumentAsync(It.IsAny<DocumentService.Core.DTOs.CreateDocumentDto>(), actor))
            .ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto { Id = id, Title = "Create permission fixture" });
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpClient(); builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        if (role != "") { client.DefaultRequestHeaders.Add("Fixture-User", actor.ToString()); client.DefaultRequestHeaders.Add("Fixture-Roles", role); }
        var response = await client.PostAsJsonAsync("/api/documents", new { Title = "Create permission fixture", DocTypeId = type, UserId = Guid.NewGuid(), Roles = new[] { "Admin" } });
        Assert.Equal(expected, (int)response.StatusCode);
        service.Verify(s => s.CreateDocumentAsync(It.IsAny<DocumentService.Core.DTOs.CreateDocumentDto>(), actor), expected == 201 ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData("Specialist",true,200)][InlineData("Specialist",false,403)]
    [InlineData("Clerk",true,403)][InlineData("Admin",false,200)]
    [InlineData("Manager",true,403)][InlineData("BoardOfDirectors",true,403)][InlineData("",true,401)]
    public async Task OutgoingSubmitRequiresSpecialistOwnerOrAdmin(string role,bool owns,int expected)
    {
        var actor=Guid.NewGuid();var id=Guid.NewGuid();var service=new Mock<IDocumentService>();
        service.Setup(s=>s.GetDocumentByIdAsync(id)).ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto{Id=id,DocTypeName="CongVanDi",CreatedById=owns?actor:Guid.NewGuid(),Status="Draft"});
        service.Setup(s=>s.SubmitForReviewAsync(id,actor,It.IsAny<string?>())).ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto{Id=id,Status="PendingDeptReview"});
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddHttpClient();builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();if(role!=""){client.DefaultRequestHeaders.Add("Fixture-User",actor.ToString());client.DefaultRequestHeaders.Add("Fixture-Roles",role);}
        using var response=await client.PostAsJsonAsync($"/api/documents/{id}/submit",new{comment="Fixture",userId=Guid.NewGuid(),roles=new[]{"Admin"}});
        Assert.Equal(expected,(int)response.StatusCode);
        service.Verify(s=>s.SubmitForReviewAsync(id,actor,It.IsAny<string?>()),expected==200?Times.Once():Times.Never());
    }

    [Theory]
    [InlineData("Clerk","CongVanDen","CongVanDi")]
    [InlineData("Specialist","CongVanDi","CongVanDen")]
    public async Task EditingCannotSwitchDirectionToBypassCreationRole(string role,string original,string target)
    {
        var actor=Guid.NewGuid();var id=Guid.NewGuid();var type=Guid.NewGuid();var service=new Mock<IDocumentService>();
        service.Setup(s=>s.GetDocumentByIdAsync(id)).ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto{Id=id,DocTypeName=original});
        service.Setup(s=>s.GetDocumentTypesAsync()).ReturnsAsync(new[]{new DocumentService.Core.DTOs.DocumentTypeDto{Id=type,TypeName=target}});
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddHttpClient();builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();client.DefaultRequestHeaders.Add("Fixture-User",actor.ToString());client.DefaultRequestHeaders.Add("Fixture-Roles",role);
        using var response=await client.PutAsJsonAsync($"/api/documents/{id}",new{Title="Fixture",DocTypeId=type});
        Assert.Equal(403,(int)response.StatusCode);
        service.Verify(s=>s.UpdateDocumentAsync(It.IsAny<Guid>(),It.IsAny<DocumentService.Core.DTOs.UpdateDocumentDto>(),It.IsAny<Guid>()),Times.Never());
    }

    [Fact]
    public async Task PurgeEndpointRequiresAdmin_ValidatedConfirmation_AndJwtActor()
    {
        var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        var service = new Mock<IDocumentPurgeService>();
        service.Setup(s => s.PurgeAsync(id, It.IsAny<DocumentActor>(), It.IsAny<DocumentService.Core.DTOs.DocumentPurgeDto>()))
            .ReturnsAsync(new DocumentService.Core.DTOs.DocumentPurgeResult(id, false));
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentPurgeController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        var body = new { Reason = "Remove fixture", ConfirmationTitle = "Fixture", ExpectedVersion = Guid.Empty, UserId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/documents/{id}/purge", body)).StatusCode);
        client.DefaultRequestHeaders.Add("Fixture-User", actor.ToString());
        foreach (var role in new[] { "Clerk", "Specialist", "Manager", "BoardOfDirectors" })
        {
            client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/purge", body)).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/documents/{id}/purge", new { Reason = "Missing", ConfirmationTitle = "Fixture" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/purge", body)).StatusCode);
        service.Verify(s => s.PurgeAsync(id, It.Is<DocumentActor>(a => a.UserId == actor && a.IsAdmin),
            It.Is<DocumentService.Core.DTOs.DocumentPurgeDto>(r => r.ExpectedVersion == Guid.Empty && r.Reason == body.Reason)), Times.Once);
    }

    [Fact]
    public async Task CancelEndpointRequiresAdminAndUsesJwtActor()
    {
        var id = Guid.NewGuid(); var actor = Guid.NewGuid(); var version = Guid.NewGuid();
        var service = new Mock<IDocumentService>();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpClient(); builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        object body = new { Reason = "Wrong publication", ExpectedVersion = version, UserId = Guid.NewGuid(), Roles = new[] { "Admin" } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", body)).StatusCode);
        client.DefaultRequestHeaders.Add("Fixture-User", actor.ToString());
        foreach (var role in new[] { "Clerk", "Specialist", "Manager", "BoardOfDirectors" })
        {
            client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", body)).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", new { ExpectedVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", new { Reason = "Missing version" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", new { Reason = "Null version", ExpectedVersion = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", new { Reason = "Legacy", ExpectedVersion = Guid.Empty })).StatusCode);
        service.Verify(s => s.CancelDocumentAsync(id, It.Is<DocumentActor>(a => a.UserId == actor && a.IsAdmin), "Legacy", Guid.Empty), Times.Once);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", body)).StatusCode);
        service.Verify(s => s.CancelDocumentAsync(id, It.Is<DocumentActor>(a => a.UserId == actor && a.IsAdmin), "Wrong publication", version), Times.Once);
    }

    [Fact]
    public async Task IncomingSubmitRequiresClerkOrAdminAndClerkCannotUseTheAssignmentApi()
    {
        var actor = Guid.NewGuid(); var version = Guid.NewGuid(); var id = Guid.NewGuid();
        var service = new Mock<IDocumentService>();
        service.Setup(s => s.SubmitIncomingAsync(id, It.Is<DocumentActor>(a => a.UserId == actor && (a.Roles.Contains("Clerk") || a.IsAdmin)), version, "Fixture"))
            .ReturnsAsync(new DocumentService.Core.DTOs.DocumentDto { Id = id, Status = DocumentStatus.PendingDirection });
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpClient(); builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("Fixture-User", actor.ToString());
        foreach (var role in new[] { "Specialist", "Manager", "BoardOfDirectors" })
        {
            client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/incoming-submit", new { ExpectedVersion = version, Comment = "Fixture" })).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", "Admin");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/incoming-submit", new { ExpectedVersion = version, Comment = "Fixture" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Fixture-Roles"); client.DefaultRequestHeaders.Add("Fixture-Roles", "Clerk");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/documents/{id}/incoming-submit", new { ExpectedVersion = version, Comment = "Fixture", UserId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/documents/{id}/assign", new { ToUserId = Guid.NewGuid() })).StatusCode);
        service.Verify(s => s.SubmitIncomingAsync(id, It.IsAny<DocumentActor>(), version, "Fixture"), Times.Exactly(2));
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
        // A real controller -> service -> repository call for the legacy zero-version record.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var legacy = await db.Documents.SingleAsync(d => d.Id == id);
            legacy.Status = DocumentStatus.Published; legacy.ConcurrencyVersion = Guid.Empty;
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync($"/api/documents/{id}/cancel", new { Reason = "Legacy cancellation", ExpectedVersion = Guid.Empty })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var deletedJson = System.Text.Json.JsonDocument.Parse(await (await client.GetAsync("/api/documents/deleted")).Content.ReadAsStringAsync());
        deletedJson.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("isCancelled").GetBoolean().Should().BeTrue();
    }
}
