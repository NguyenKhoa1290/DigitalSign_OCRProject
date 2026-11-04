using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentService.API.Services;
using DocumentService.Core.Common;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using Moq;
using Npgsql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DocumentService.API.Controllers;

namespace DocumentService.Tests;

public partial class OcrJobTests
{
    private static MemoryStream Word(string text="Edited text",bool macro=false)
    {
        var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            using(var output=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))output.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            using(var output=new StreamWriter(zip.CreateEntry("word/document.xml").Open()))output.Write($"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>");
            if(macro)zip.CreateEntry("word/vbaProject.bin");
        }
        stream.Position=0;return stream;
    }
    private sealed class EditFiles
    {
        public Mock<IFileStorageService> Storage=new();public Dictionary<string,byte[]> Objects=new();public List<string> Deleted=new();public int Uploaded;
        public Func<Task>? AfterUpload;
        public EditFiles()
        {
            Objects["source.pdf"]=Encoding.UTF8.GetBytes("Original signed PDF fixture");
            Storage.Setup(s=>s.UploadFileAsync(It.IsAny<Stream>(),It.IsAny<string>(),It.IsAny<string>(),"documents")).Returns(async(Stream input,string _,string _,string _) =>
            {
                var name=Guid.NewGuid()+".docx";using var bytes=new MemoryStream();await input.CopyToAsync(bytes);Objects[name]=bytes.ToArray();Uploaded++;
                if(AfterUpload!=null)await AfterUpload();return name;
            });
            Storage.Setup(s=>s.DownloadFileAsync(It.IsAny<string>(),"documents")).ReturnsAsync((string name,string _)=>(Stream)new MemoryStream(Objects[name]));
            Storage.Setup(s=>s.DeleteFileAsync(It.IsAny<string>(),"documents")).ReturnsAsync((string name,string _)=>{Deleted.Add(name);Objects.Remove(name);return true;});
        }
    }
    private static async Task<OcrEditedFileStore> Edits(Fixture f,EditFiles files)
    {
        await f.Sql("UPDATE \"Documents\" SET \"OcrDataRaw\"='"+Result+"'");
        await OcrEditedFileStore.InitializeAsync(f.Source);return new(f.Source,files.Storage.Object);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void EditedWordRequiresValidDocxAndRejectsMacros(bool macro)
    {
        using var word=Word(macro:macro);
        if(macro)Assert.Throws<DocumentServiceException>(()=>OcrEditedFileStore.ValidateWord(word));
        else OcrEditedFileStore.ValidateWord(word);
        using var invalid=new MemoryStream(Encoding.UTF8.GetBytes("renamed PDF"));
        Assert.Throws<DocumentServiceException>(()=>OcrEditedFileStore.ValidateWord(invalid));
    }
    [PostgresFact]
    public async Task EditedWordStoresServerActorAuditAndKeepsOriginalAndLayout()
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);using var word=Word();
        var edit=await edits.UploadAsync(f.Doc,Actor,f.Version,word,"Bản đã sửa.DOCX",default);
        Assert.Equal(Actor,edit.EditedById);Assert.Equal("Requester",edit.EditedByName);Assert.Equal("Clerk",edit.EditorRole);Assert.Equal(1,edit.Revision);
        Assert.Equal("documents/source.pdf",await f.Scalar<string>("SELECT \"MinioPath\" FROM \"Documents\""));
        Assert.Equal("original.pdf",await f.Scalar<string>("SELECT \"FileName\" FROM \"Documents\""));
        Assert.Equal("OcrEdited",await f.Scalar<string>("SELECT \"Action\" FROM \"DocumentProcesses\" WHERE \"Action\"<>'Create'"));
        Assert.Contains("Result",await f.Scalar<string>("SELECT \"OcrDataRaw\"::text FROM \"Documents\""));Assert.True(files.Objects.ContainsKey("source.pdf"));
        var downloaded=await edits.DownloadAsync(f.Doc,edit.Id,default);using var content=downloaded.Content;
        Assert.Equal("Bản đã sửa.DOCX",downloaded.FileName);using var copy=new MemoryStream();await content.CopyToAsync(copy);Assert.Equal(word.ToArray(),copy.ToArray());
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
    }
    [PostgresFact]
    public async Task ReplacementQueuesOldWordAndCleanupNeverDeletesOriginalOrCurrentEdit()
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);using var one=Word("one");using var two=Word("two");
        var first=await edits.UploadAsync(f.Doc,Actor,f.Version,one,"one.docx",default);
        var version=await f.Scalar<Guid>("SELECT \"ConcurrencyVersion\" FROM \"Documents\"");
        var second=await edits.UploadAsync(f.Doc,Actor,version,two,"two.docx",default);Assert.Equal(2,second.Revision);
        Assert.True(await edits.CleanupOneAsync(default));Assert.Equal(new[]{first.MinioPath[10..]},files.Deleted);
        Assert.True(files.Objects.ContainsKey("source.pdf"));Assert.True(files.Objects.ContainsKey(second.MinioPath[10..]));
        await Assert.ThrowsAsync<DocumentConflictException>(()=>edits.DownloadAsync(f.Doc,first.Id,default));
        await f.Sql($"INSERT INTO \"OcrEditedFileCleanup\" (\"ObjectName\",\"DocumentId\") VALUES ('{second.MinioPath[10..]}','{f.Doc}')");
        Assert.True(await edits.CleanupOneAsync(default));Assert.Single(files.Deleted); // Unknown commit outcome must preserve the live Word.
        await f.Sql("DELETE FROM \"Documents\"");Assert.True(await edits.CleanupOneAsync(default));Assert.Contains(second.MinioPath[10..],files.Deleted);
    }
    [PostgresFact]
    public async Task FailedConcurrentSaveQueuesOrphanAndDoesNotMarkResultEdited()
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);
        files.AfterUpload=()=>f.Sql("UPDATE \"Documents\" SET \"ConcurrencyVersion\"=gen_random_uuid()");using var word=Word();
        await Assert.ThrowsAsync<DocumentConflictException>(()=>edits.UploadAsync(f.Doc,Actor,f.Version,word,"edited.docx",default));
        Assert.DoesNotContain("editedDocument",await f.Scalar<string>("SELECT \"OcrDataRaw\"::text FROM \"Documents\""));
        Assert.Equal(1,files.Uploaded);Assert.True(await edits.CleanupOneAsync(default));Assert.Single(files.Objects);Assert.True(files.Objects.ContainsKey("source.pdf"));
    }
    [PostgresFact]
    public async Task MissingOcrStaleVersionWrongRoleAndRunningOcrAreRejectedBeforeStorage()
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);
        using var word=Word();await Assert.ThrowsAsync<DocumentConflictException>(()=>edits.UploadAsync(f.Doc,Actor,Guid.NewGuid(),word,"e.docx",default));
        await f.Enqueue();await Assert.ThrowsAsync<DocumentConflictException>(()=>edits.UploadAsync(f.Doc,Actor,f.Version,word,"e.docx",default));
        await f.Sql("DELETE FROM \"OcrJobs\"; UPDATE \"Documents\" SET \"OcrDataRaw\"=NULL");
        await Assert.ThrowsAsync<DocumentServiceException>(()=>edits.UploadAsync(f.Doc,Actor,f.Version,word,"e.docx",default));
        await f.Sql("UPDATE \"AppRoles\" SET \"RoleName\"='Specialist'");
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>edits.UploadAsync(f.Doc,Actor,f.Version,word,"e.docx",default));Assert.Equal(0,files.Uploaded);
    }
    [PostgresFact]
    public async Task RerunningAiPreservesHumanWordForSameSourceButChangedAttachmentInvalidatesIt()
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);using var word=Word();
        var edit=await edits.UploadAsync(f.Doc,Actor,f.Version,word,"human.docx",default);
        var version=await f.Scalar<Guid>("SELECT \"ConcurrencyVersion\" FROM \"Documents\"");
        var next=await f.Store.EnqueueAsync(f.Doc,Actor,new(version,"gemini-fixture","1"),default);await using var connection=await f.Store.OpenAsync(default);
        var job=(await f.Store.ClaimAsync(connection,next.Id,default))!;await f.Store.CompleteAsync(connection,job,Result,default);
        Assert.Contains(edit.Id.ToString(),await f.Scalar<string>("SELECT \"OcrDataRaw\"::text FROM \"Documents\""));Assert.False(await edits.CleanupOneAsync(default));
        await f.Sql("UPDATE \"Documents\" SET \"MinioPath\"='documents/new.pdf',\"ConcurrencyVersion\"=gen_random_uuid()");
        await Assert.ThrowsAsync<DocumentConflictException>(()=>edits.DownloadAsync(f.Doc,edit.Id,default));Assert.True(await edits.CleanupOneAsync(default));
        Assert.Contains(edit.MinioPath[10..],files.Deleted);
    }
    [Fact]
    public void OcrPayloadCannotForgeEditorIdentityOrReplaceServerManagedWord()
    {
        var old=JsonNode.Parse("""{"editedDocument":{"sourcePath":"documents/a.pdf","editedByName":"Real clerk"}}""")!;
        var incoming=JsonNode.Parse("""{"provider":"gemini","EditedDocument":{"editedByName":"Forged actor"}}""")!.AsObject();
        var safe=OcrEditMetadata.Protect(incoming,old,"documents/a.pdf");Assert.Contains("Real clerk",safe.ToJsonString());Assert.DoesNotContain("Forged",safe.ToJsonString());
        var replaced=OcrEditMetadata.Protect(new JsonObject(),old,"documents/new.pdf");Assert.Null(replaced["editedDocument"]);
    }
    [PostgresTheory]
    [InlineData("Clerk",200)][InlineData("Admin",200)][InlineData("Specialist",403)][InlineData("Manager",403)][InlineData(null,401)]
    public async Task EditedWordHttpUploadEnforcesRoleAndReturnsSavedActor(string? role,int expected)
    {
        await using var f=await Fixture.Create();var files=new EditFiles();var edits=await Edits(f,files);
        if(role=="Admin")await f.Sql("UPDATE \"AppRoles\" SET \"RoleName\"='Admin'");
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        builder.Services.AddSingleton(edits);builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,Auth>("Fixture",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddControllers().AddApplicationPart(typeof(OcrEditedFilesController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=new HttpClient{BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        if(role!=null)client.DefaultRequestHeaders.Add("Fixture-Role",role);
        using var word=Word();using var body=new MultipartFormDataContent();body.Add(new ByteArrayContent(word.ToArray()),"file","edited.docx");
        using var response=await client.PostAsync($"/api/documents/{f.Doc}/ocr-edited-file?expectedVersion={f.Version}",body);
        Assert.Equal(expected,(int)response.StatusCode);
        if(expected==200){var json=await response.Content.ReadAsStringAsync();Assert.Contains("Requester",json);Assert.Contains("edited.docx",json);Assert.Equal(1,files.Uploaded);}
        else Assert.Equal(0,files.Uploaded);
    }
}
