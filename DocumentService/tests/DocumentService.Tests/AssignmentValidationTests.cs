using System.Net.Http.Json;
using DocumentService.API.Controllers;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DocumentService.Tests;

public partial class DocumentDeletionApiTests
{
    [Theory]
    [InlineData("Admin", 410)]
    [InlineData("Manager", 410)]
    [InlineData("BoardOfDirectors", 410)]
    [InlineData("Clerk", 403)]
    [InlineData("Specialist", 403)]
    [InlineData("", 401)]
    public async Task UnsignedAssignmentIsRetiredWithoutDirectoryOrServiceWrites(string role, int expected)
    {
        var service = new Mock<IDocumentService>(MockBehavior.Strict);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(service.Object); builder.Services.AddSingleton(factory.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        if (role != "")
        {
            client.DefaultRequestHeaders.Add("Fixture-User", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add("Fixture-Roles", role);
        }
        using var response = await client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/assign",
            new { ToUserId = Guid.NewGuid(), Comment = "Unsigned assignment", Roles = new[] { "Admin" } });
        Assert.Equal(expected, (int)response.StatusCode);
        service.VerifyNoOtherCalls(); factory.VerifyNoOtherCalls();
    }
}
