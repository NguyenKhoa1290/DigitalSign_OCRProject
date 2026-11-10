using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentService.API.Controllers;
using DocumentService.API.Services;
using DocumentService.Core.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace DocumentService.Tests;

public partial class OcrJobTests
{
    private static readonly string Connection=Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES") ?? "";
    private static readonly Guid Actor=Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly string Result="""{"provider":"gemini","modelId":"gemini-fixture","selectedPages":[1],"layout":{"blocks":[{"type":"paragraph","text":"Result"}]}}""";

    private sealed class Fixture : IAsyncDisposable
    {
        public NpgsqlDataSource Source=null!;
        public OcrJobStore Store=null!;
        public Guid Doc=Guid.NewGuid(),Version=Guid.NewGuid();
        public string Schema="jobs_"+Guid.NewGuid().ToString("N");
        public static async Task<Fixture> Create()
        {
            var configured=new NpgsqlConnectionStringBuilder(Connection);
            if(configured.Database!="fixture" || configured.Host is not ("127.0.0.1" or "localhost"))
                throw new InvalidOperationException("Use a local isolated fixture database for OCR queue tests.");
            var f=new Fixture();
            await using(var c=new NpgsqlConnection(Connection))
            {await c.OpenAsync();await using var cmd=new NpgsqlCommand($"CREATE SCHEMA \"{f.Schema}\"",c);await cmd.ExecuteNonQueryAsync();}
            var config=new NpgsqlConnectionStringBuilder(Connection){SearchPath=f.Schema};
            f.Source=NpgsqlDataSource.Create(config.ConnectionString);f.Store=new(f.Source);
            await f.Sql("""
                CREATE TABLE "AppUsers" ("Id" uuid PRIMARY KEY,"IsActive" boolean NOT NULL,"FullName" text,"DepartmentId" uuid,"Username" text DEFAULT 'fixture');
                CREATE TABLE "AppRoles" ("Id" uuid PRIMARY KEY,"RoleName" text NOT NULL);
                CREATE TABLE "AppUserRoles" ("UserId" uuid,"RoleId" uuid);
                CREATE TABLE "DocumentTypes" ("Id" uuid PRIMARY KEY,"TypeName" text NOT NULL);
                INSERT INTO "DocumentTypes" VALUES ('aaaaaaaa-0000-0000-0000-000000000001','CongVanDen'),('aaaaaaaa-0000-0000-0000-000000000002','CongVanDi');
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY,"Title" text NOT NULL,"MinioPath" text NOT NULL,"FileName" text,"ConcurrencyVersion" uuid NOT NULL,"IsDeleted" boolean NOT NULL DEFAULT false,"OcrDataRaw" jsonb,"Status" text DEFAULT 'Draft',"ProcessingDepartmentId" uuid,"DocTypeId" uuid DEFAULT 'aaaaaaaa-0000-0000-0000-000000000001');
                CREATE TABLE "DocumentProcesses" ("Id" uuid PRIMARY KEY,"DocId" uuid REFERENCES "Documents"("Id") ON DELETE CASCADE,"FromUserId" uuid,"ToUserId" uuid,"Action" text,"Comment" text,"Timestamp" timestamptz);
                ALTER TABLE "Documents" ADD COLUMN "SigningStartedAt" timestamptz NULL, ADD COLUMN "SignedPdfPath" text NULL;
                CREATE TABLE "Signatures" ("DocId" uuid NOT NULL);
                """);
            var monitoring=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../ApiGateway/Monitoring/schema.sql"));
            await f.Sql(await File.ReadAllTextAsync(monitoring));
            await f.Store.InitializeAsync();
            await using var seed=f.Source.CreateCommand("""
                INSERT INTO "AppUsers" ("Id","IsActive","FullName","DepartmentId") VALUES (@actor,true,'Requester',NULL);
                INSERT INTO "AppUsers" ("Id","IsActive","FullName","DepartmentId") VALUES (@creator,true,'Different document creator',NULL);
                INSERT INTO "AppRoles" VALUES (@role,'Clerk'); INSERT INTO "AppUserRoles" VALUES (@actor,@role);
                INSERT INTO "Documents" ("Id","Title","MinioPath","FileName","ConcurrencyVersion") VALUES (@doc,'Fixture','documents/source.pdf','original.pdf',@version);
                INSERT INTO "DocumentProcesses" VALUES (@process,@doc,@creator,NULL,'Create','Create',now());
                """);
            seed.Parameters.AddWithValue("actor",Actor);seed.Parameters.AddWithValue("creator",Guid.NewGuid());seed.Parameters.AddWithValue("role",Guid.NewGuid());seed.Parameters.AddWithValue("process",Guid.NewGuid());seed.Parameters.AddWithValue("doc",f.Doc);seed.Parameters.AddWithValue("version",f.Version);await seed.ExecuteNonQueryAsync();
            return f;
        }
        public async Task Sql(string sql){await using var c=Source.CreateCommand(sql);await c.ExecuteNonQueryAsync();}
        public async Task<T> Scalar<T>(string sql){await using var c=Source.CreateCommand(sql);return (T)(await c.ExecuteScalarAsync())!;}
        public Task<OcrJobDto> Enqueue()=>Store.EnqueueAsync(Doc,Actor,new(Version,"gemini-fixture","1"),default);
        public async ValueTask DisposeAsync(){await Source.DisposeAsync();await using var c=new NpgsqlConnection(Connection);await c.OpenAsync();await using var cmd=new NpgsqlCommand($"DROP SCHEMA \"{Schema}\" CASCADE",c);await cmd.ExecuteNonQueryAsync();}
    }

    [PostgresFact]
    public async Task SignedContentCannotStartAnotherOcrJobEvenForAdmin()
    {
        await using var f=await Fixture.Create();
        await f.Sql("UPDATE \"AppRoles\" SET \"RoleName\"='Admin'; UPDATE \"Documents\" SET \"Status\"='Rejected',\"SigningStartedAt\"=now(),\"SignedPdfPath\"='signed.pdf'");
        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>f.Enqueue());
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"OcrJobs\""));
    }

    [Theory]
    [InlineData("3,1-2,2","1,2,3")]
    [InlineData("2147483647","2147483647")]
    public void SelectionCanonicalizesWithoutExpandingUnboundedRanges(string input,string expected)=>Assert.Equal(expected,OcrJobStore.CanonicalPages(input));
    [Theory]
    [InlineData("1-6")][InlineData("0")][InlineData("1,2,3,4,5,6")][InlineData("1-2147483647")][InlineData("")]
    public void InvalidSelectionDoesNotQueue(string value)=>Assert.Throws<DocumentServiceException>(()=>OcrJobStore.CanonicalPages(value));

    [PostgresFact]
    public async Task QueueReturnsImmediatelyDeduplicatesAndSurvivesStoreRecreation()
    {
        await using var f=await Fixture.Create();
        var jobs=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>f.Enqueue()));
        Assert.Single(jobs.Select(j=>j.Id).Distinct());Assert.All(jobs,j=>Assert.Equal("Queued",j.Status));
        var recreated=new OcrJobStore(f.Source);Assert.Equal(jobs[0].Id,(await recreated.LatestAsync(f.Doc,default))!.Id);
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
    }

    [PostgresFact]
    public async Task CompletionPreservesConcurrentMetadataAndNotifiesRequesterOnceAfterCommit()
    {
        await using var f=await Fixture.Create();await f.Enqueue();
        await using var connection=await f.Store.OpenAsync(default);
        var job=(await f.Store.ClaimAsync(connection,(await f.Store.LatestAsync(f.Doc,default))!.Id,default))!;
        await f.Sql("UPDATE \"Documents\" SET \"Title\"='Edited while OCR runs',\"ConcurrencyVersion\"=gen_random_uuid()");
        await f.Store.CompleteAsync(connection,job,Result,default);
        await f.Store.CompleteAsync(connection,job,Result,default);
        Assert.Equal("Succeeded",(await f.Store.LatestAsync(f.Doc,default))!.Status);
        Assert.Equal("Edited while OCR runs",await f.Scalar<string>("SELECT \"Title\" FROM \"Documents\""));
        Assert.Contains("Result",await f.Scalar<string>("SELECT \"OcrDataRaw\"::text FROM \"Documents\""));
        Assert.Equal(1L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
        Assert.Equal(Actor,await f.Scalar<Guid>("SELECT \"UserId\" FROM \"UserNotifications\""));
        Assert.Equal("OcrCompleted",await f.Scalar<string>("SELECT \"Kind\" FROM \"UserNotifications\""));
        Assert.Equal("GeminiOcrCompleted",await f.Scalar<string>("SELECT \"Action\" FROM \"DocumentProcesses\" WHERE \"Action\"<>'Create'"));
    }

    [PostgresFact]
    public async Task ReplacementOrDeletionNeverReceivesOldResultAndFailureIsVisible()
    {
        await using var f=await Fixture.Create();await f.Enqueue();
        await using var connection=await f.Store.OpenAsync(default);var job=(await f.Store.ClaimAsync(connection,(await f.Store.LatestAsync(f.Doc,default))!.Id,default))!;
        await f.Sql("UPDATE \"Documents\" SET \"MinioPath\"='new.pdf',\"ConcurrencyVersion\"=gen_random_uuid()");
        await Assert.ThrowsAsync<DocumentConflictException>(()=>f.Store.CompleteAsync(connection,job,Result,default));
        await f.Store.FailAsync(connection,job,"Tệp đã thay đổi",default);
        await f.Store.FailAsync(connection,job,"Duplicate",default);
        Assert.Equal("Failed",(await f.Store.LatestAsync(f.Doc,default))!.Status);
        Assert.Equal(1L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\" WHERE \"Kind\"='OcrFailed' AND \"DocumentId\" IS NULL"));
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"Documents\" WHERE \"OcrDataRaw\" IS NOT NULL"));
    }

    [PostgresFact]
    public async Task InterruptedRunningFailsOnceWhileQueuedWorkRemainsAndWorkerLockIsExclusive()
    {
        await using var f=await Fixture.Create();await f.Enqueue();await using var one=await f.Store.OpenAsync(default);var job=await f.Store.ClaimAsync(one,(await f.Store.LatestAsync(f.Doc,default))!.Id,default);
        await using var two=await f.Store.OpenAsync(default);
        await using(var cmd=new NpgsqlCommand($"SELECT pg_try_advisory_lock({OcrJobStore.WorkerLock})",one))Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        await using(var cmd=new NpgsqlCommand($"SELECT pg_try_advisory_lock({OcrJobStore.WorkerLock})",two))Assert.False((bool)(await cmd.ExecuteScalarAsync())!);
        await f.Store.RecoverInterruptedAsync(one,default);await f.Store.RecoverInterruptedAsync(one,default);
        Assert.Equal("Failed",(await f.Store.LatestAsync(f.Doc,default))!.Status);Assert.Equal(1L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
        var next=await f.Enqueue();Assert.Equal("Queued",next.Status);Assert.NotEqual(job!.Info.Id,next.Id);
        await using(var cmd=new NpgsqlCommand($"SELECT pg_advisory_unlock({OcrJobStore.WorkerLock})",one))await cmd.ExecuteScalarAsync();
    }

    [PostgresFact]
    public async Task MissingFileStaleAndInactiveOperatorCannotQueue()
    {
        await using var f=await Fixture.Create();
        await Assert.ThrowsAsync<DocumentConflictException>(()=>f.Store.EnqueueAsync(f.Doc,Actor,new(Guid.NewGuid(),"gemini-fixture","1"),default));
        await f.Sql("UPDATE \"Documents\" SET \"MinioPath\"=''");await Assert.ThrowsAsync<DocumentServiceException>(()=>f.Enqueue());
        await f.Sql("UPDATE \"AppUsers\" SET \"IsActive\"=false");await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(()=>f.Enqueue());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedWorkerCompletesOrFailsIndependentlyAndEmitsRealtimeNotification(bool fail)
    {
        await using var f=await Fixture.Create();
        var queued=await f.Enqueue();
        var started=new TaskCompletionSource<HttpRequestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new OcrHttp(started,release,fail);
        var queue=new FakeQueue();await f.Store.PublishNextAsync(queue,default);
        using var worker=new OcrJobWorker(f.Store,queue,new OcrClients(handler),Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrJobWorker>.Instance);
        await using var listener=await f.Source.OpenConnectionAsync();
        await using(var listen=new NpgsqlCommand("LISTEN hau_notifications",listener))await listen.ExecuteNonQueryAsync();
        var notification=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Notification+=(_,_)=>notification.TrySetResult();
        await worker.StartAsync(default);
        try
        {
            var request=await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.EndsWith("/api/ocr/internal/gemini-job",request.RequestUri!.ToString());
            Assert.Equal("fixture-service",request.Headers.GetValues("X-Service-Token").Single());
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("Running",(await f.Store.LatestAsync(f.Doc,default))!.Status);
            // A separate request can still edit metadata while the AI response is pending.
            await f.Sql("UPDATE \"Documents\" SET \"Title\"='Still usable',\"ConcurrencyVersion\"=gen_random_uuid()");
            Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
            release.TrySetResult();
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while((await f.Store.LatestAsync(f.Doc,deadline.Token))!.Status is "Queued" or "Running")
                await Task.Delay(40,deadline.Token);
            Assert.Equal(fail?"Failed":"Succeeded",(await f.Store.LatestAsync(f.Doc,default))!.Status);
            Assert.Equal("Still usable",await f.Scalar<string>("SELECT \"Title\" FROM \"Documents\""));
            Assert.Equal(fail?"OcrFailed":"OcrCompleted",await f.Scalar<string>("SELECT \"Kind\" FROM \"UserNotifications\""));
            Assert.Equal(Actor,await f.Scalar<Guid>("SELECT \"UserId\" FROM \"UserNotifications\""));
            Assert.Equal(fail?0L:1L,await f.Scalar<long>("SELECT count(*) FROM \"Documents\" WHERE \"OcrDataRaw\" IS NOT NULL"));
            while(!notification.Task.IsCompleted)await listener.WaitAsync(deadline.Token);
        }
        finally {release.TrySetResult();await worker.StopAsync(default);}
    }

    private sealed class FakeQueue : IOcrJobQueue
    {
        public Channel<OcrJobDelivery> Messages=Channel.CreateUnbounded<OcrJobDelivery>();
        public int Published,Committed;
        public bool FailPublish,FailAfterPublish;
        public Task PublishAsync(Guid id,CancellationToken token)
        {
            if(FailPublish)throw new HttpRequestException("Fixture broker unavailable");
            Published++;Messages.Writer.TryWrite(new(id,new object()));
            if(FailAfterPublish)throw new HttpRequestException("Fixture lost ACK after broker persisted message");
            return Task.CompletedTask;
        }
        public Task<IOcrJobSubscription> SubscribeAsync(CancellationToken token)=>Task.FromResult<IOcrJobSubscription>(new Subscription(this));
        private sealed class Subscription(FakeQueue queue):IOcrJobSubscription
        {
            public async Task<OcrJobDelivery?> ReceiveAsync(CancellationToken token)=>await queue.Messages.Reader.ReadAsync(token);
            public void Commit(OcrJobDelivery _)=>Interlocked.Increment(ref queue.Committed);
            public void Dispose(){}
        }
    }

    [PostgresFact]
    public async Task KafkaOutboxRetainsFailedPublicationAndRepublishesOnlyUnstartedJobs()
    {
        await using var f=await Fixture.Create();var job=await f.Enqueue();var queue=new FakeQueue{FailPublish=true};
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Store.PublishNextAsync(queue,default));
        Assert.Equal("Queued",(await f.Store.LatestAsync(f.Doc,default))!.Status);
        Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"OcrJobs\" WHERE \"PublishedAt\" IS NOT NULL"));
        queue.FailPublish=false;Assert.True(await f.Store.PublishNextAsync(queue,default));
        Assert.False(await f.Store.PublishNextAsync(queue,default));Assert.Equal(1,queue.Published);
        await f.Sql("UPDATE \"OcrJobs\" SET \"PublishedAt\"=now()-interval '6 minutes'");
        Assert.True(await f.Store.PublishNextAsync(queue,default));Assert.Equal(2,queue.Published);
        await using var connection=await f.Store.OpenAsync(default);
        Assert.NotNull(await f.Store.ClaimAsync(connection,job.Id,default));
        Assert.Null(await f.Store.ClaimAsync(connection,job.Id,default));
        Assert.False(await f.Store.PublishNextAsync(queue,default));
    }

    [PostgresFact]
    public async Task DuplicateKafkaCommandsInvokeAiAndNotifyOnlyOnce()
    {
        await using var f=await Fixture.Create();var job=await f.Enqueue();var queue=new FakeQueue{FailAfterPublish=true};
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Store.PublishNextAsync(queue,default));
        queue.FailAfterPublish=false;Assert.True(await f.Store.PublishNextAsync(queue,default));
        Assert.Equal(2,queue.Published);
        var started=new TaskCompletionSource<HttpRequestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);release.SetResult();
        var handler=new OcrHttp(started,release,false);
        using var worker=new OcrJobWorker(f.Store,queue,new OcrClients(handler),Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrJobWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while(Volatile.Read(ref queue.Committed)<2)await Task.Delay(40,deadline.Token);
            Assert.Equal(1,handler.Calls);Assert.Equal(1L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
            Assert.Equal("Succeeded",(await f.Store.LatestAsync(f.Doc,default))!.Status);
        }
        finally{await worker.StopAsync(default);}
    }

    [KafkaPostgresTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RealKafkaOutboxConsumerCommitAndNotificationWithoutRealAi(bool fail)
    {
        await using var f=await Fixture.Create();var job=await f.Enqueue();
        var servers=Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_KAFKA")!;
        if(!servers.StartsWith("localhost:")&&!servers.StartsWith("127.0.0.1:"))throw new InvalidOperationException("Only a local test broker is allowed.");
        var topic="ocr-fixture-"+Guid.NewGuid().ToString("N");var group=topic+"-group";
        using var admin=new AdminClientBuilder(new AdminClientConfig{BootstrapServers=servers}).Build();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {{"KafkaSettings:BootstrapServers",servers},{"KafkaSettings:TopicOcrRequested",topic},{"KafkaSettings:OcrConsumerGroup",group}}).Build();
        using var queue=new OcrJobKafkaQueue(config);
        var started=new TaskCompletionSource<HttpRequestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var handler=new OcrHttp(started,release,fail);
        using var worker=new OcrJobWorker(f.Store,queue,new OcrClients(handler),Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrJobWorker>.Instance);
        using var outbox=new OcrJobOutbox(f.Store,queue,Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrJobOutbox>.Instance);
        try
        {
            // Exercise fresh-topic creation and the TopicAlreadyExists startup path.
            using(var prepared=await queue.SubscribeAsync(default)){}
            // A malformed envelope is acknowledged without blocking the valid command behind it.
            using(var producer=new ProducerBuilder<string,string>(new ProducerConfig{BootstrapServers=servers}).Build())
                await producer.ProduceAsync(topic,new Message<string,string>{Key="poison",Value="{\"schemaVersion\":999999999999}"});
            await worker.StartAsync(default);await outbox.StartAsync(default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(25));
            Assert.Equal("Running",(await f.Store.LatestAsync(f.Doc,default))!.Status);
            // Redelivery while the HTTP/AI response is pending must not start another call.
            await queue.PublishAsync(job.Id,default);release.SetResult();
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while((await f.Store.LatestAsync(f.Doc,deadline.Token))!.Status is "Queued" or "Running")await Task.Delay(50,deadline.Token);
            using var observer=new ConsumerBuilder<string,string>(new ConsumerConfig{BootstrapServers=servers,GroupId=group,EnableAutoCommit=false}).Build();
            var partition=new TopicPartition(topic,0);
            while(observer.Committed(new[]{partition},TimeSpan.FromSeconds(5))[0].Offset.Value<3)await Task.Delay(100,deadline.Token);
            Assert.Equal(fail?"Failed":"Succeeded",(await f.Store.LatestAsync(f.Doc,default))!.Status);
            Assert.Equal(1,handler.Calls);Assert.Equal(1L,await f.Scalar<long>("SELECT count(*) FROM \"UserNotifications\""));
            Assert.Equal(fail?"OcrFailed":"OcrCompleted",await f.Scalar<string>("SELECT \"Kind\" FROM \"UserNotifications\""));
        }
        finally
        {
            release.TrySetResult();await outbox.StopAsync(default);await worker.StopAsync(default);
            await admin.DeleteGroupsAsync(new[]{group});await admin.DeleteTopicsAsync(new[]{topic});
        }
    }

    private sealed class OcrHttp(TaskCompletionSource<HttpRequestMessage> started,TaskCompletionSource release,bool fail):HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var body=await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("documents/source.pdf",body);Assert.Contains("gemini-fixture",body);
            started.TrySetResult(request);await release.Task.WaitAsync(token);
            return new HttpResponseMessage(fail?HttpStatusCode.TooManyRequests:HttpStatusCode.OK)
            {Content=new StringContent(fail?"{\"detail\":\"Fixture quota exhausted\"}":Result,System.Text.Encoding.UTF8,"application/json")};
        }
    }
    private sealed class OcrClients(HttpMessageHandler handler):IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("ocr-background",name);
            var client=new HttpClient(handler,false){BaseAddress=new Uri("http://fixture-ocr/")};
            client.DefaultRequestHeaders.Add("X-Service-Token","fixture-service");return client;
        }
    }

    [PostgresTheory]
    [InlineData("Clerk",202)][InlineData("Admin",202)][InlineData("Specialist",403)][InlineData("Manager",403)][InlineData(null,401)]
    public async Task HttpAcknowledgesQueuedWorkWithoutWaitingForAnyAi(string? role,int expected)
    {
        await using var f=await Fixture.Create();var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        builder.Services.AddSingleton(f.Store);builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,Auth>("Fixture",_=>{});builder.Services.AddAuthorization();builder.Services.AddControllers().AddApplicationPart(typeof(OcrJobsController).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=new HttpClient {BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        if(role!=null)client.DefaultRequestHeaders.Add("Fixture-Role",role);
        var timer=Stopwatch.StartNew();using var response=await client.PostAsJsonAsync($"api/documents/{f.Doc}/ocr-jobs",new {expectedVersion=f.Version,modelId="gemini-fixture",pages="1"});
        Assert.Equal((HttpStatusCode)expected,response.StatusCode);Assert.True(timer.Elapsed<TimeSpan.FromSeconds(5));
        if(expected==202){var body=await response.Content.ReadAsStringAsync();Assert.Contains("Queued",body);Assert.Equal(0L,await f.Scalar<long>("SELECT count(*) FROM \"Documents\" WHERE \"OcrDataRaw\" IS NOT NULL"));}
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.TryGetValue("Fixture-Role",out var role)?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Actor.ToString()),new Claim(ClaimTypes.Role,role.ToString())},Scheme.Name)),Scheme.Name)));
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES")))Skip="Set HAU_OCR_JOB_TEST_POSTGRES to an isolated test database (provided in GitHub Actions).";}
}
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES")))Skip="Set HAU_OCR_JOB_TEST_POSTGRES to an isolated test database (provided in GitHub Actions).";}
}
public sealed class KafkaPostgresTheoryAttribute : TheoryAttribute
{
    public KafkaPostgresTheoryAttribute()
    {if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_POSTGRES")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_OCR_JOB_TEST_KAFKA")))Skip="Set both HAU_OCR_JOB_TEST_POSTGRES and HAU_OCR_JOB_TEST_KAFKA to isolated local fixtures.";}
}
