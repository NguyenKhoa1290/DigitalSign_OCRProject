using DocumentService.API.Services;
using DocumentService.Core.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public partial class OcrJobTests
{
    [PostgresFact]
    public async Task PostgresFileSwapChecksCurrentStageAndVersionAtomically()
    {
        await using var f=await Fixture.Create();
        await f.Sql("UPDATE \"Documents\" SET \"DocTypeId\"='aaaaaaaa-0000-0000-0000-000000000002'");
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(f.Source).Options);
        var repo=new DocumentRepository(db);
        Assert.True(await repo.ReplaceFileAsync(f.Doc,"documents/source.pdf","documents/new.pdf","new.pdf",f.Version));
        Assert.False(await repo.ReplaceFileAsync(f.Doc,"documents/new.pdf","documents/stale.pdf","stale.pdf",f.Version));
        var current=await f.Scalar<Guid>("SELECT \"ConcurrencyVersion\" FROM \"Documents\"");
        await f.Sql("UPDATE \"Documents\" SET \"Status\"='DirectorSigned'");
        Assert.False(await repo.ReplaceFileAsync(f.Doc,"documents/new.pdf","documents/unsigned.pdf","unsigned.pdf",current));
        Assert.Equal("documents/new.pdf",await f.Scalar<string>("SELECT \"MinioPath\" FROM \"Documents\""));
    }

    [PostgresFact]
    public async Task ClerkCannotQueueOrEditOutgoingButAdminCanSupportIt()
    {
        await using var f=await Fixture.Create();
        await f.Sql("UPDATE \"Documents\" SET \"DocTypeId\"='aaaaaaaa-0000-0000-0000-000000000002'");
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>f.Enqueue());
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"OcrJobs\""));
        var storage=new EditFiles();var edits=new OcrEditedFileStore(f.Source,storage.Storage.Object);
        using var word=Word();
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>edits.UploadAsync(f.Doc,Actor,f.Version,word,"fixed.docx",default));
        Assert.Equal(0,storage.Uploaded);
        await f.Sql("UPDATE \"AppRoles\" SET \"RoleName\"='Admin'");
        var job=await f.Enqueue();Assert.Equal("Queued",job.Status);
    }

    [PostgresFact]
    public async Task FinishedAiCannotSaveAfterClerkLosesDocumentDirection()
    {
        await using var f=await Fixture.Create();var queued=await f.Enqueue();
        await using var connection=await f.Source.OpenConnectionAsync();
        var job=(await f.Store.ClaimAsync(connection,queued.Id,default))!;
        await f.Sql("UPDATE \"Documents\" SET \"DocTypeId\"='aaaaaaaa-0000-0000-0000-000000000002'");
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>f.Store.CompleteAsync(connection,job,Result,default));
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"Documents\" WHERE \"OcrDataRaw\" IS NOT NULL"));
    }

    [PostgresFact]
    public async Task QueuedClerkWorkLosingItsDirectionIsFailedBeforeAnyAiCall()
    {
        await using var f=await Fixture.Create();var job=await f.Enqueue();var queue=new FakeQueue();
        Assert.True(await f.Store.PublishNextAsync(queue,default));
        await f.Sql("UPDATE \"Documents\" SET \"DocTypeId\"='aaaaaaaa-0000-0000-0000-000000000002'");
        var started=new TaskCompletionSource<HttpRequestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);release.SetResult();
        var handler=new OcrHttp(started,release,false);
        using var worker=new OcrJobWorker(f.Store,queue,new OcrClients(handler),NullLogger<OcrJobWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while(Volatile.Read(ref queue.Committed)<1)await Task.Delay(40,deadline.Token);
            Assert.Equal(0,handler.Calls);Assert.Equal("Failed",(await f.Store.LatestAsync(f.Doc,default))!.Status);
            Assert.Equal("OcrFailed",await f.Scalar<string>("SELECT \"Kind\" FROM \"UserNotifications\""));
        }
        finally{await worker.StopAsync(default);}
    }
}
