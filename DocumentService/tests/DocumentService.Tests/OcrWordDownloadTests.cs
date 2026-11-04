using System.Net;
using System.Net.Http.Json;
using DocumentService.API.Controllers;
using DocumentService.API.Services;
using DocumentService.Core.DTOs;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;

namespace DocumentService.Tests;

public partial class DocumentDeletionApiTests
{
    private sealed class WordHttp : HttpMessageHandler,IHttpClientFactory
    {
        public int Calls;
        public HttpClient CreateClient(string name)=>new(this,false){BaseAddress=new Uri("http://fixture-ocr/")};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Assert.Equal("/api/ocr/internal/export-layout",request.RequestUri!.AbsolutePath);
            Assert.Contains("stored-only",await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})};
        }
    }

    [Theory]
    [InlineData("Specialist",true,false,200)][InlineData("Specialist",false,false,403)]
    [InlineData("Specialist",true,true,403)][InlineData("Clerk",false,false,200)]
    [InlineData("Admin",false,false,200)][InlineData("Manager",true,false,403)]
    [InlineData("BoardOfDirectors",true,false,403)][InlineData("",true,false,401)]
    public async Task SavedLayoutDownloadDoesNotGrantOcrOrTrustCallerLayout(string roles,bool owns,bool incoming,int expected)
    {
        var actor=Guid.NewGuid();var id=Guid.NewGuid();var version=Guid.NewGuid();
        var doc=new DocumentDto{Id=id,CreatedById=owns?actor:Guid.NewGuid(),DocTypeName=incoming?"CongVanDen":"CongVanDi",
            ConcurrencyVersion=version,MinioPath="documents/scan.pdf",OcrDataRaw="""{"provider":"gemini","sourcePath":"documents/scan.pdf","layout":{"blocks":[{"type":"paragraph","text":"stored-only"}]}}"""};
        var documents=new Mock<IDocumentService>();documents.Setup(d=>d.GetDocumentByIdAsync(id)).ReturnsAsync(doc);
        await using var source=NpgsqlDataSource.Create("Host=127.0.0.1;Database=unused_word_fixture;Username=fixture;Password=fixture-only");
        var edits=new OcrEditedFileStore(source,Mock.Of<IFileStorageService>());var http=new WordHttp();
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddSingleton(documents.Object);builder.Services.AddSingleton(edits);
        builder.Services.AddSingleton<IHttpClientFactory>(http);builder.Services.AddControllers().AddApplicationPart(typeof(OcrWordController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();
        if(roles!=""){client.DefaultRequestHeaders.Add("Fixture-User",actor.ToString());client.DefaultRequestHeaders.Add("Fixture-Roles",roles);}
        async Task<HttpResponseMessage> Get(Guid v)=>await client.GetAsync($"/api/documents/{id}/ocr-word?expectedVersion={v}&createdById={actor}&layout=untrusted&role=Admin");
        using var response=await Get(version);Assert.Equal(expected,(int)response.StatusCode);
        Assert.Equal(expected==200?1:0,http.Calls);
        if(expected==200)
        {
            Assert.Equal(new byte[]{1,2,3},await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(OcrEditedFileStore.WordMime,response.Content.Headers.ContentType!.MediaType);
            using var stale=await Get(Guid.NewGuid());Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
            doc.MinioPath="documents/ready.docx";using var word=await Get(version);Assert.Equal(HttpStatusCode.BadRequest,word.StatusCode);
            Assert.Equal(1,http.Calls);
        }
    }
}
