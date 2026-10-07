using System.Net;
using System.Security.Claims;
using DocumentService.API.Controllers;
using DocumentService.Core.DTOs;
using DocumentService.Core.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentService.Tests;

public class AssignmentValidationTests
{
    [Theory]
    [InlineData(404, "{}", 400)]
    [InlineData(200, "{\"data\":{\"isActive\":false}}", 400)]
    [InlineData(500, "{}", 503)]
    [InlineData(200, "{}", 503)]
    [InlineData(200, "{\"data\":null}", 503)]
    [InlineData(200, "[]", 503)]
    [InlineData(200, "invalid json", 503)]
    [InlineData(401, "{}", 401)]
    public async Task InvalidOrUnverifiedRecipient_DoesNotCreateAssignment(int identityStatus, string body, int expectedStatus)
    {
        var service = new Mock<IDocumentService>();
        var controller = CreateController(service, identityStatus, body);
        var result = await controller.Assign(Guid.NewGuid(), new WorkflowActionDto { ToUserId = Guid.NewGuid() });
        var actualStatus = result is ObjectResult objectResult ? objectResult.StatusCode : ((StatusCodeResult)result).StatusCode;
        actualStatus.Should().Be(expectedStatus);
        service.Verify(s => s.AssignAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ActiveRecipient_ForwardsActorRecipientCommentAndAuthentication()
    {
        var service = new Mock<IDocumentService>();
        var docId = Guid.NewGuid(); var recipient = Guid.NewGuid();
        service.Setup(s => s.AssignAsync(docId, It.IsAny<Guid>(), recipient, "Review"))
            .ReturnsAsync(new DocumentDto { Id = docId });
        var handler = new DirectoryHandler(200, "{\"data\":{\"isActive\":true}}");
        var controller = CreateController(service, handler);
        var result = await controller.Assign(docId, new WorkflowActionDto { ToUserId = recipient, Comment = "Review" });
        result.Should().BeOfType<OkObjectResult>();
        handler.Authorization.Should().Be("Bearer test-token");
        handler.Path.Should().Be($"/api/users/{recipient}");
        service.Verify(s => s.AssignAsync(docId, It.IsAny<Guid>(), recipient, "Review"), Times.Once);
    }

    private static DocumentsController CreateController(Mock<IDocumentService> service, int status, string body)
        => CreateController(service, new DirectoryHandler(status, body));

    private static DocumentsController CreateController(Mock<IDocumentService> service, DirectoryHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("identity-directory"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://identity.test/") });
        var controller = new DocumentsController(service.Object, NullLogger<DocumentsController>.Instance,
            new ConfigurationBuilder().Build(), factory.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "test"));
        controller.Request.Headers.Authorization = "Bearer test-token";
        return controller;
    }

    private sealed class DirectoryHandler(int status, string body) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
