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

public class DocumentUploadLimitTests
{
    [Theory]
    [InlineData(9L * 1024 * 1024, 200)]
    [InlineData(50L * 1024 * 1024, 200)]
    [InlineData(50L * 1024 * 1024 + 1, 413)]
    public async Task Upload_Enforces50MiBFileLimit(long length, int expectedStatus)
    {
        var actor = Guid.NewGuid(); var id = Guid.NewGuid();
        var service = new Mock<IDocumentService>();
        service.Setup(s => s.UploadFileAsync(id, It.IsAny<Stream>(), "test.pdf", "application/pdf", actor))
            .ReturnsAsync(new DocumentDto { Id = id });
        var controller = new DocumentsController(service.Object, NullLogger<DocumentsController>.Instance,
            new ConfigurationBuilder().Build(), Mock.Of<IHttpClientFactory>());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor.ToString()) }, "fixture"))
        }};
        var file = new FormFile(Stream.Null, 0, length, "file", "test.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var result = (ObjectResult)await controller.UploadFile(id, file);
        result.StatusCode.Should().Be(expectedStatus);
        service.Verify(s => s.UploadFileAsync(id, It.IsAny<Stream>(), "test.pdf", "application/pdf", actor),
            expectedStatus == 200 ? Times.Once() : Times.Never());
    }
}
