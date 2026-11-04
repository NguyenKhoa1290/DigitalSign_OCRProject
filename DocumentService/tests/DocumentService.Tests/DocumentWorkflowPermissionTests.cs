using DocumentService.Core.Entities;
using System.Net;
using System.Net.Http.Json;
using DocumentService.API.Controllers;
using DocumentService.Core.DTOs;
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
    [InlineData("Clerk",true,false,200)][InlineData("Clerk",false,true,403)]
    [InlineData("Specialist",false,true,200)][InlineData("Specialist",false,false,403)]
    [InlineData("Specialist",true,true,403)][InlineData("Admin",false,false,200)]
    [InlineData("Admin",true,false,200)][InlineData("Manager",false,true,403)]
    [InlineData("BoardOfDirectors",false,true,403)][InlineData("",false,true,401)]
    [InlineData("Clerk,Specialist",false,true,200)]
    public async Task MetadataAndFileChangesUseDirectionAndActualAuthor(string roles,bool incoming,bool owns,int expected)
    {
        var actor=Guid.NewGuid();var id=Guid.NewGuid();var type=Guid.NewGuid();
        var doc=new DocumentDto{Id=id,DocTypeId=type,DocTypeName=incoming?"CongVanDen":"CongVanDi",CreatedById=owns?actor:Guid.NewGuid(),Status="Draft"};
        var service=new Mock<IDocumentService>();service.Setup(s=>s.GetDocumentByIdAsync(id)).ReturnsAsync(doc);
        service.Setup(s=>s.GetDocumentTypesAsync()).ReturnsAsync(new[]{new DocumentTypeDto{Id=type,TypeName=doc.DocTypeName}});
        service.Setup(s=>s.UpdateDocumentAsync(id,It.IsAny<UpdateDocumentDto>(),It.Is<DocumentActor>(a=>a.UserId==actor))).ReturnsAsync(doc);
        service.Setup(s=>s.UploadFileAsync(id,It.IsAny<Stream>(),It.IsAny<string>(),It.IsAny<string>(),It.Is<DocumentActor>(a=>a.UserId==actor))).ReturnsAsync(doc);
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddHttpClient();builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();
        if(roles!=""){client.DefaultRequestHeaders.Add("Fixture-User",actor.ToString());client.DefaultRequestHeaders.Add("Fixture-Roles",roles);}
        using var update=await client.PutAsJsonAsync($"/api/documents/{id}",new{title="Updated",docTypeId=type,userId=actor,roles=new[]{"Admin"}});
        Assert.Equal(expected,(int)update.StatusCode);
        using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(new byte[]{1,2,3}),"file","draft.pdf");
        using var upload=await client.PostAsync($"/api/documents/{id}/upload",form);Assert.Equal(expected,(int)upload.StatusCode);
        service.Verify(s=>s.UpdateDocumentAsync(id,It.IsAny<UpdateDocumentDto>(),It.Is<DocumentActor>(a=>a.UserId==actor)),expected==200?Times.Once():Times.Never());
        service.Verify(s=>s.UploadFileAsync(id,It.IsAny<Stream>(),It.IsAny<string>(),It.IsAny<string>(),It.Is<DocumentActor>(a=>a.UserId==actor)),expected==200?Times.Once():Times.Never());
    }

    [Theory]
    [InlineData("Clerk",true,200)][InlineData("Clerk",false,403)]
    [InlineData("Admin",false,200)][InlineData("Specialist",false,403)]
    [InlineData("Specialist",true,403)][InlineData("Manager",true,403)]
    [InlineData("BoardOfDirectors",true,403)][InlineData("",true,401)]
    public async Task AttachedOcrAndCallbackCannotBypassRoleOrDirection(string roles,bool incoming,int expected)
    {
        var actor=Guid.NewGuid();var id=Guid.NewGuid();var doc=new DocumentDto{Id=id,DocTypeName=incoming?"CongVanDen":"CongVanDi"};
        var service=new Mock<IDocumentService>();service.Setup(s=>s.GetDocumentByIdAsync(id)).ReturnsAsync(doc);
        service.Setup(s=>s.UpdateOcrDataAsync(id,It.IsAny<UpdateOcrDto>(),actor)).ReturnsAsync(doc);
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddHttpClient();builder.Services.AddSingleton(service.Object);
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();
        if(roles!=""){client.DefaultRequestHeaders.Add("Fixture-User",actor.ToString());client.DefaultRequestHeaders.Add("Fixture-Roles",roles);}
        using var state=await client.GetAsync($"/api/documents/{id}/ocr-state");Assert.Equal(expected,(int)state.StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Patch,$"/api/documents/{id}/ocr"){Content=JsonContent.Create(new{ocrDataRaw="{}",roles=new[]{"Admin"}})};
        using var response=await client.SendAsync(request);Assert.Equal(expected,(int)response.StatusCode);
        service.Verify(s=>s.UpdateOcrDataAsync(id,It.IsAny<UpdateOcrDto>(),actor),expected==200?Times.Once():Times.Never());
    }
}
