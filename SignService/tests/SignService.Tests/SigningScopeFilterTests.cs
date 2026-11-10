using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using SignService.API.Controllers;
using SignService.API.Filters;
using SignService.Core.DTOs;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

namespace SignService.Tests;

public class SigningScopeFilterTests
{
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims=new[]{new Claim(ClaimTypes.NameIdentifier,Request.Headers["Actor"].ToString()),new Claim(ClaimTypes.Role,Request.Headers["Role"].ToString())};
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }

    [Fact]
    public async Task PersonalSignChecksActualSignerDepartmentAndStatusBeforeCallingSigningService()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var department=Guid.NewGuid();var other=Guid.NewGuid();var root=Guid.NewGuid();var manager=Guid.NewGuid();var admin=Guid.NewGuid();var board=Guid.NewGuid();var doc=Guid.NewGuid();
        await using(var documentDb=new DocumentService.Infrastructure.Data.AppDbContext(new DbContextOptionsBuilder<DocumentService.Infrastructure.Data.AppDbContext>().UseSqlite(connection).Options)) {
            await documentDb.Database.EnsureCreatedAsync();
            documentDb.Documents.Add(new(){Id=doc,Title="Document",DepartmentId=department,Status="PendingDeptReview",MinioPath="a.pdf",DocTypeId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")});
            await documentDb.SaveChangesAsync();
        }
        await using(var db=new AppDbContext(options)){
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (Id TEXT PRIMARY KEY, DocId TEXT NOT NULL, SignerId TEXT NOT NULL, SignatureType TEXT NOT NULL, SignedAt TEXT NOT NULL, SignedFilePath TEXT NULL)");
        }
        var directory=new Mock<IWorkflowDirectory>();
        var users=new Dictionary<Guid,WorkflowUser>{[manager]=new(manager,"Actual manager",department,true,["Manager"]),[other]=new(other,"Other",Guid.NewGuid(),true,["Manager"]),[root]=new(root,"School",Guid.NewGuid(),true,["Manager"]),[admin]=new(admin,"Admin",null,true,["Admin"]),[board]=new(board,"Board",null,true,["BoardOfDirectors"])};
        directory.Setup(d=>d.GetUserAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id)=>users[id]);
        var signer=new Mock<ISignService>();SignRequestDto? captured=null;
        signer.Setup(s=>s.PersonalSignAsync(It.IsAny<SignRequestDto>())).Callback<SignRequestDto>(r=>captured=r).ReturnsAsync(new SignResultDto());
        signer.Setup(s=>s.LegalSealAsync(It.IsAny<SignRequestDto>())).Callback<SignRequestDto>(r=>captured=r).ReturnsAsync(new SignResultDto());
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Services.AddDbContext<AppDbContext>(o=>o.UseSqlite(connection));
        builder.Services.AddSingleton(directory.Object);builder.Services.AddSingleton(signer.Object);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,Auth>("fixture",_=>{});builder.Services.AddAuthorization();
        builder.Services.AddControllers(o=>o.Filters.Add<SigningScopeFilter>()).AddApplicationPart(typeof(SignaturesController).Assembly);
        await using var app=builder.Build();app.Use(async(ctx,next)=>{try{await next(ctx);}catch(IncomingAccessException){ctx.Response.StatusCode=403;}catch(IncomingConflictException){ctx.Response.StatusCode=409;}catch(DocumentNotFoundException){ctx.Response.StatusCode=404;}});
        app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();using var client=app.GetTestClient();
        async Task<HttpStatusCode> Send(Guid actor,string role,Guid forged){client.DefaultRequestHeaders.Clear();client.DefaultRequestHeaders.Add("Actor",actor.ToString());client.DefaultRequestHeaders.Add("Role",role);return (await client.PostAsJsonAsync("/api/signatures/personal-sign",new{docId=doc,signerId=forged,signerName="Forged"})).StatusCode;}
        Assert.Equal(HttpStatusCode.Forbidden,await Send(other,"Manager",manager));Assert.Null(captured);
        Assert.Equal(HttpStatusCode.OK,await Send(manager,"Manager",other));Assert.Equal(manager,captured!.SignerId);Assert.Equal("Actual manager",captured.SignerName);
        await using(var db=new AppDbContext(options)){var d=await db.DocumentFiles.SingleAsync();d.IsSchoolWide=true;d.DepartmentId=users[root].DepartmentId;await db.SaveChangesAsync();}
        captured=null;Assert.Equal(HttpStatusCode.Forbidden,await Send(manager,"Manager",root));Assert.Null(captured);
        Assert.Equal(HttpStatusCode.OK,await Send(root,"Manager",other));Assert.Equal(root,captured!.SignerId);
        await using(var db=new AppDbContext(options)){var d=await db.DocumentFiles.SingleAsync();d.Status="Draft";await db.SaveChangesAsync();}
        captured=null;Assert.Equal(HttpStatusCode.Conflict,await Send(root,"Manager",root));Assert.Null(captured);
        Assert.Equal(HttpStatusCode.Conflict,await Send(admin,"Admin",root));Assert.Null(captured);
        await using(var db=new AppDbContext(options)){var d=await db.DocumentFiles.SingleAsync();d.Status="PendingDeptReview";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.OK,await Send(admin,"Admin",root));Assert.Equal(admin,captured!.SignerId);
        client.DefaultRequestHeaders.Clear();client.DefaultRequestHeaders.Add("Actor",board.ToString());client.DefaultRequestHeaders.Add("Role","BoardOfDirectors");
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/signatures/legal-seal",new{docId=doc,signerId=manager})).StatusCode);
        await using(var db=new AppDbContext(options)){var d=await db.DocumentFiles.SingleAsync();d.Status="PendingDirectorSign";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/signatures/legal-seal",new{docId=doc,signerId=manager})).StatusCode);
        Assert.Equal(board,captured!.SignerId);
    }
}
