using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentService.API.Controllers;
using DocumentService.API.Filters;
using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Extensions;
using DocumentService.Infrastructure.Repositories;
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

namespace DocumentService.Tests;

public class DocumentScopeTests
{
    public sealed class Envelope<T> { public T? Data { get; set; } }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue("Actor",out var id))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new[]{new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Role,Request.Headers["Role"].ToString())};
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Guid Root=Guid.NewGuid(), A=Guid.NewGuid(), B=Guid.NewGuid(), Author=Guid.NewGuid(), Reader=Guid.NewGuid(), Other=Guid.NewGuid(), RootUser=Guid.NewGuid();
        public readonly Guid OwnDoc=Guid.NewGuid(), OtherDoc=Guid.NewGuid(), CommonDoc=Guid.NewGuid(), UnknownDoc=Guid.NewGuid();
        public readonly SqliteConnection Connection=new("Data Source=:memory:");
        public WebApplication App=null!;
        public HttpClient Client=null!;
        public AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
        public void Actor(Guid id,string role){Client.DefaultRequestHeaders.Remove("Actor");Client.DefaultRequestHeaders.Remove("Role");Client.DefaultRequestHeaders.Add("Actor",id.ToString());Client.DefaultRequestHeaders.Add("Role",role);}
        public async Task Initialize()
        {
            await Connection.OpenAsync();
            await using(var db=Db()){
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Signatures (DocId TEXT NOT NULL, SignerId TEXT NOT NULL, SignedAt TEXT NOT NULL, SignatureType TEXT NOT NULL, SignedFilePath TEXT NULL)");
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, IsActive INTEGER NOT NULL, DepartmentId TEXT NULL, FullName TEXT, Username TEXT)");
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE Departments (Id TEXT PRIMARY KEY, DeptName TEXT NOT NULL, ParentId TEXT NULL)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Departments VALUES ({Root},'Trường',NULL),({A},'Phòng A',{Root}),({B},'Phòng B',{Root})");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppUsers VALUES ({Author},1,{A},'Author','author'),({Reader},1,{A},'Reader','reader'),({Other},1,{B},'Other','other'),({RootUser},1,{Root},'Root','root')");
                foreach(var (id,dept,common) in new[]{(OwnDoc,(Guid?)A,false),(OtherDoc,(Guid?)B,false),(CommonDoc,(Guid?)Root,true),(UnknownDoc,(Guid?)null,false)})
                    db.Documents.Add(new(){Id=id,Title=id==CommonDoc?"Common":"Unit document",DocTypeId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),DepartmentId=dept,DepartmentName=common?"Trường":"Unit",IsSchoolWide=common,Status="PendingDeptReview",CreatedById=dept==A?Author:Guid.NewGuid(),CreatedAt=DateTime.UtcNow});
                await db.SaveChangesAsync();
            }
            var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<AppDbContext>(o=>o.UseSqlite(Connection));
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,Auth>("fixture",_=>{});builder.Services.AddAuthorization();
            builder.Services.AddControllers(o=>{o.Filters.Add<DocumentScopeFilter>();o.Filters.Add<DocumentCreatorFilter>();}).AddApplicationPart(typeof(DocumentsController).Assembly);
            builder.Services.AddScoped<IDocumentRepository,DocumentRepository>();builder.Services.AddScoped<IDocumentTypeRepository,DocumentTypeRepository>();builder.Services.AddScoped<IDocumentProcessRepository,DocumentProcessRepository>();
            builder.Services.AddScoped<IDocumentService,DocumentService.Infrastructure.Services.DocumentService>();
            builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=fixture"));
            builder.Services.AddSingleton<DocumentService.API.Services.OcrJobStore>();builder.Services.AddScoped<DocumentService.API.Services.OcrEditedFileStore>();
            builder.Services.AddSingleton(Mock.Of<IFileStorageService>());builder.Services.AddSingleton(Mock.Of<IKafkaProducerService>());builder.Services.AddHttpClient();
            App=builder.Build();
            App.Use(async(ctx,next)=>{try{await next(ctx);}catch(DocumentNotFoundException){ctx.Response.StatusCode=404;}catch(UnauthorizedDocumentAccessException){ctx.Response.StatusCode=403;}catch(DocumentConflictException){ctx.Response.StatusCode=409;}catch(DocumentServiceException){ctx.Response.StatusCode=400;}});
            App.UseAuthentication();App.UseAuthorization();App.MapControllers();await App.StartAsync();Client=App.GetTestClient();
        }
        public async ValueTask DisposeAsync(){Client?.Dispose();if(App!=null)await App.DisposeAsync();await Connection.DisposeAsync();}
    }

    [Theory][InlineData("Manager")][InlineData("Specialist")][InlineData("Clerk")]
    public async Task ListDetailFilesAndOcrRejectOtherUnit_WhileCommonRemainsReadable(string role)
    {
        await using var f=new Fixture();await f.Initialize();f.Actor(f.Reader,role);
        var page=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents?pageSize=1"))!.Data!;
        Assert.Equal(2,page.TotalCount);Assert.Single(page.Items);
        var all=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents"))!.Data!.Items.ToList();
        Assert.Contains(all,d=>d.Id==f.OwnDoc);Assert.Contains(all,d=>d.Id==f.CommonDoc);
        Assert.Equal(role=="Manager",all.Single(d=>d.Id==f.OwnDoc).CanReview);Assert.False(all.Single(d=>d.Id==f.CommonDoc).CanReview);
        foreach(var path in new[]{"", "/file", "/ocr-jobs/latest", "/ocr-word", "/ocr-edited-file?revisionId="+Guid.NewGuid()})
            Assert.Equal(path.StartsWith("/ocr-edited-file") && role!="Clerk" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound,(await f.Client.GetAsync("/api/documents/"+f.OtherDoc+path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await f.Client.GetAsync("/api/documents/"+f.UnknownDoc)).StatusCode);
        var common=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents?scope=school"))!.Data!;
        Assert.Equal(f.CommonDoc,Assert.Single(common.Items).Id);
        var stats=(await f.Client.GetFromJsonAsync<Envelope<DocumentStatisticsDto>>("/api/documents/stats"))!.Data!;
        Assert.Equal(2,stats.TotalDocuments);
        Assert.Equal(1,stats.PendingDeptDocuments);
    }

    [Fact]
    public async Task CreationIgnoresClientScope_AndLaterUserMoveDoesNotChangeDocumentScope()
    {
        await using var f=new Fixture();await f.Initialize();f.Actor(f.Author,"Specialist");
        var response=await f.Client.PostAsJsonAsync("/api/documents",new{title="New",docTypeId="aaaaaaaa-0000-0000-0000-000000000002",departmentId=f.B,departmentName="Forged",isSchoolWide=true});
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var created=(await response.Content.ReadFromJsonAsync<Envelope<DocumentDto>>())!.Data!;
        Assert.Equal(f.A,created.DepartmentId);Assert.Equal("Phòng A",created.DepartmentName);Assert.False(created.IsSchoolWide);
        await using(var db=f.Db())await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET DepartmentId={f.B} WHERE Id={f.Author}");
        var update=await f.Client.PutAsJsonAsync("/api/documents/"+created.Id,new{title="Edited",docTypeId=created.DocTypeId,departmentId=f.B,isSchoolWide=true});
        Assert.Equal(HttpStatusCode.OK,update.StatusCode);
        var edited=(await update.Content.ReadFromJsonAsync<Envelope<DocumentDto>>())!.Data!;
        Assert.Equal(f.A,edited.DepartmentId);Assert.False(edited.IsSchoolWide);
        f.Actor(f.RootUser,"Specialist");
        var root=await f.Client.PostAsJsonAsync("/api/documents",new{title="School",docTypeId=created.DocTypeId});
        var common=(await root.Content.ReadFromJsonAsync<Envelope<DocumentDto>>())!.Data!;
        Assert.Equal(f.Root,common.DepartmentId);Assert.True(common.IsSchoolWide);
    }

    [Fact]
    public async Task SigningQueueReviewAndHistoryUseActualActorAndFrozenUnit()
    {
        await using var f=new Fixture();await f.Initialize();f.Actor(f.Reader,"Manager");
        var pending=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents/signing?view=pending"))!.Data!;
        Assert.Equal(f.OwnDoc,Assert.Single(pending.Items).Id);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.PostAsJsonAsync("/api/documents/"+f.CommonDoc+"/dept-sign",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await f.Client.PostAsJsonAsync("/api/documents/"+f.OtherDoc+"/dept-sign",new{})).StatusCode);
        await using(var db=f.Db()){
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures (DocId,SignerId,SignedAt,SignatureType) VALUES ({f.OwnDoc},{f.Reader},{DateTime.UtcNow},'PersonalSignature')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures (DocId,SignerId,SignedAt,SignatureType) VALUES ({f.CommonDoc},{f.Other},{DateTime.UtcNow},'PersonalSignature')");
        }
        var history=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents/signing?view=signed&signingUserId="+f.Other))!.Data!;
        Assert.Equal(f.OwnDoc,Assert.Single(history.Items).Id);Assert.NotNull(history.Items.Single().MySignedAt);
        f.Actor(f.RootUser,"Manager");
        var root=(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents/signing?view=pending"))!.Data!;
        Assert.Equal(f.CommonDoc,Assert.Single(root.Items).Id);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Client.PostAsJsonAsync("/api/documents/"+f.CommonDoc+"/dept-sign",new{})).StatusCode);
        await using(var db=f.Db()){
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Signatures VALUES ({f.CommonDoc},{f.RootUser},{DateTime.UtcNow},'PersonalSignature','signed.pdf')");
            var document=await db.Documents.SingleAsync(d=>d.Id==f.CommonDoc);document.SignedPdfPath="signed.pdf";document.SigningStartedAt=DateTime.UtcNow;await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK,(await f.Client.PostAsJsonAsync("/api/documents/"+f.CommonDoc+"/dept-sign",new{})).StatusCode);
        f.Actor(f.Reader,"Admin");
        Assert.Equal(4,(await f.Client.GetFromJsonAsync<Envelope<PagedResult<DocumentDto>>>("/api/documents"))!.Data!.TotalCount);
    }
}
