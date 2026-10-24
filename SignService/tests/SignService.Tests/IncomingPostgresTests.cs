using System.Collections.Concurrent;
using System.Text;
using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using SignService.Core.DTOs;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Services;
using DocContext = DocumentService.Infrastructure.Data.AppDbContext;
using SignContext = SignService.Infrastructure.Data.AppDbContext;

namespace SignService.Tests;

public sealed class IncomingPostgresFactAttribute : FactAttribute
{
    public IncomingPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAU_INCOMING_TEST_PG")))
            Skip = "Set HAU_INCOMING_TEST_PG to a disposable doc_deletion_tests database on a non-production port.";
    }
}

public class IncomingPostgresTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _schema = "incoming_" + Guid.NewGuid().ToString("N");
        private readonly string _admin;
        private readonly string _connection;
        public readonly Guid Id = Guid.NewGuid(), Board = Guid.NewGuid(), Manager = Guid.NewGuid(), Clerk = Guid.NewGuid(), Specialist = Guid.NewGuid(), WrongManager = Guid.NewGuid();
        public readonly Guid Department = Guid.NewGuid();
        public readonly ConcurrentDictionary<string, byte[]> Objects = new();
        private readonly Mock<IWorkflowDirectory> _directory = new();
        private readonly Mock<IMinioService> _minio = new();
        private readonly Mock<IPdfSigningService> _pdf = new();
        private readonly Mock<ICertificateService> _certificates = new();
        public DocContext Docs() => new(new DbContextOptionsBuilder<DocContext>().UseNpgsql(_connection).Options);
        public SignContext Signs() => new(new DbContextOptionsBuilder<SignContext>().UseNpgsql(_connection).Options);
        public IncomingWorkflow Service(SignContext db) => new(db, _directory.Object, _minio.Object, _pdf.Object, _certificates.Object);
        public Fixture()
        {
            var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_INCOMING_TEST_PG"));
            if (config.Database != "doc_deletion_tests" || config.Host != "127.0.0.1" || config.Port == 5432)
                throw new InvalidOperationException("Only the isolated disposable test DB is allowed.");
            _admin = config.ConnectionString; config.SearchPath = _schema; _connection = config.ConnectionString;
        }
        public async Task Initialize()
        {
            await using (var admin = new NpgsqlConnection(_admin))
            {
                await admin.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE SCHEMA {_schema}", admin); await command.ExecuteNonQueryAsync();
            }
            await using var docs = Docs(); await docs.Database.MigrateAsync();
            await using var signs = Signs(); await signs.Database.MigrateAsync();
            Assert.False(docs.Database.HasPendingModelChanges()); Assert.False(signs.Database.HasPendingModelChanges());
            await docs.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "AppUsers" ("Id" uuid PRIMARY KEY,"IsActive" boolean NOT NULL,"DepartmentId" uuid);
                CREATE TABLE "AppRoles" ("Id" uuid PRIMARY KEY,"RoleName" text NOT NULL);
                CREATE TABLE "AppUserRoles" ("UserId" uuid NOT NULL,"RoleId" uuid NOT NULL)
                """);
            var users = new[] { new WorkflowUser(Board,"Board",null,true,["BoardOfDirectors"]),
                new WorkflowUser(Manager,"Manager",Department,true,["Manager"]),
                new WorkflowUser(Specialist,"Specialist",Department,true,["Specialist"]),
                new WorkflowUser(Clerk,"Clerk",null,true,["Clerk"]),
                new WorkflowUser(WrongManager,"Other",Guid.NewGuid(),true,["Manager"]) };
            foreach (var user in users)
            {
                var roleId = Guid.NewGuid();
                await docs.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppUsers\" VALUES ({user.Id},true,{user.DepartmentId})");
                await docs.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppRoles\" VALUES ({roleId},{user.Roles[0]})");
                await docs.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppUserRoles\" VALUES ({user.Id},{roleId})");
            }
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName,"ApiGateway/Monitoring/schema.sql"))) root = root.Parent;
            Assert.NotNull(root);
            await docs.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"ApiGateway/Monitoring/schema.sql")));
            docs.Add(new Document { Id=Id,Title="PG incoming",MinioPath="original.pdf",CreatedById=Clerk,CreatedAt=DateTime.UtcNow,
                DocTypeId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),ConcurrencyVersion=Guid.NewGuid(),DeletionEligibilityConfirmed=true });
            docs.Add(new DocumentProcess { Id=Guid.NewGuid(),DocId=Id,FromUserId=Clerk,Action="Create",Timestamp=DateTime.UtcNow });
            await docs.SaveChangesAsync(); docs.ChangeTracker.Clear();
            var doc = (await new DocumentRepository(docs).GetByIdAsync(Id))!;
            await new DocumentRepository(docs).SubmitIncomingAsync(Id,Clerk,doc.ConcurrencyVersion,null);
            Objects["original.pdf"] = Encoding.UTF8.GetBytes("source");
            _directory.Setup(d=>d.GetUserAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id)=>users.Single(u=>u.Id==id));
            _directory.Setup(d=>d.GetDepartmentsAsync()).ReturnsAsync(new List<WorkflowDepartment>{new(Department,"PG unit",null)});
            _certificates.Setup(c=>c.GetUserCertificateAsync(It.IsAny<Guid>())).ReturnsAsync(new UserCertificate());
            _certificates.Setup(c=>c.IsCertificateValid(It.IsAny<UserCertificate>())).Returns(true);
            _minio.Setup(m=>m.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync((string key)=>Objects[key]);
            _minio.Setup(m=>m.UploadFileAsync(It.IsAny<string>(),It.IsAny<byte[]>(),"application/pdf"))
                .Callback<string,byte[],string>((key,bytes,_)=>Objects[key]=bytes).Returns(Task.CompletedTask);
            _pdf.Setup(p=>p.SignPdfAsync(It.IsAny<byte[]>(),It.IsAny<Guid>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>()))
                .Returns(async(byte[] bytes,Guid _,string type,string name,string reason)=>{await Task.Delay(75);return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)+"|"+type);});
            _pdf.Setup(p=>p.VerifyPdfSignaturesAsync(It.IsAny<byte[]>())).ReturnsAsync((byte[] bytes)=>{
                var text=Encoding.UTF8.GetString(bytes);var types=new[]{SignatureType.IncomingDirection,SignatureType.IncomingAssignment}.Where(text.Contains).ToArray();
                return new SignVerifyResult{IsValid=types.Length>0,SignatureCount=types.Length,Signatures=types.Select(t=>new SignatureInfo{SignatureType=t,IsIntact=true}).ToList()};});
        }
        public async Task<Guid> Version() { await using var db=Docs();return (await db.Documents.SingleAsync(d=>d.Id==Id)).ConcurrencyVersion; }
        public async Task<int> Notifications(Guid user)
        {
            await using var db=Docs();return await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM \"UserNotifications\" WHERE \"UserId\"={user}").SingleAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin=new NpgsqlConnection(_admin);await admin.OpenAsync();
            await using var command=new NpgsqlCommand($"DROP SCHEMA {_schema} CASCADE",admin);await command.ExecuteNonQueryAsync();
        }
    }

    [IncomingPostgresFact]
    public async Task ConcurrentDirectionSignsOnceAndNotifiesOnlyTheRoutedDepartment()
    {
        await using var f=new Fixture();await f.Initialize();var version=await f.Version();
        Assert.Equal(1,await f.Notifications(f.Board));Assert.Equal(0,await f.Notifications(f.Manager));
        async Task<Exception?> Direct()
        {
            await using var db=f.Signs();
            try{await f.Service(db).DirectAsync(f.Id,new(f.Board,["BoardOfDirectors"]),new(){ExpectedVersion=version,DepartmentId=f.Department,Comment="PG direction"});return null;}
            catch(Exception ex){return ex;}
        }
        var results=await Task.WhenAll(Direct(),Direct());
        Assert.Single(results,e=>e==null);Assert.IsType<IncomingConflictException>(results.Single(e=>e!=null));
        await using var signs=f.Signs();Assert.Equal(1,await signs.Signatures.CountAsync());
        Assert.Equal(1,await f.Notifications(f.Manager));Assert.Equal(0,await f.Notifications(f.WrongManager));
        await f.Service(signs).AssignAsync(f.Id,new(f.Manager,["Manager"]),new(){ExpectedVersion=await f.Version(),ToUserId=f.Specialist,Comment="PG assignment"});
        Assert.Equal(1,await f.Notifications(f.Specialist));Assert.Equal(2,await signs.Signatures.CountAsync());
    }

    [IncomingPostgresFact]
    public async Task AuditFailureRollsBackStatusSignatureAndNotificationWhileKeepingPriorPdf()
    {
        await using var f=new Fixture();await f.Initialize();
        await using(var db=f.Signs())await f.Service(db).DirectAsync(f.Id,new(f.Board,["BoardOfDirectors"]),new(){ExpectedVersion=await f.Version(),DepartmentId=f.Department,Comment="PG direction"});
        await using var docs=f.Docs();var before=await docs.Documents.AsNoTracking().SingleAsync(d=>d.Id==f.Id);
        await docs.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_assignment() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Action"='Assign' THEN RAISE EXCEPTION 'fixture audit failure'; END IF; RETURN NEW; END; $$;
            CREATE TRIGGER reject_assignment BEFORE INSERT ON "DocumentProcesses" FOR EACH ROW EXECUTE FUNCTION reject_assignment()
            """);
        await using var signs=f.Signs();
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Service(signs).AssignAsync(f.Id,new(f.Manager,["Manager"]),new(){ExpectedVersion=before.ConcurrencyVersion,ToUserId=f.Specialist,Comment="PG assignment"}));
        var after=await docs.Documents.AsNoTracking().SingleAsync(d=>d.Id==f.Id);
        Assert.Equal(before.Status,after.Status);Assert.Equal(before.MinioPath,after.MinioPath);Assert.Equal(before.ConcurrencyVersion,after.ConcurrencyVersion);
        Assert.True(f.Objects.ContainsKey(before.MinioPath));Assert.Equal("source",Encoding.UTF8.GetString(f.Objects["original.pdf"]));
        Assert.Equal(1,await signs.Signatures.CountAsync());Assert.Equal(0,await f.Notifications(f.Specialist));
    }
}
