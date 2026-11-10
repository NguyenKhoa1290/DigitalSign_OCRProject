using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentService.Core.Entities;
using DocumentService.Core.DTOs;
using DocumentService.Infrastructure.Repositories;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SignService.Core.Entities;
using SignService.Core.DTOs;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Services;
using SignService.Infrastructure.Repositories;
using DocContext=DocumentService.Infrastructure.Data.AppDbContext;
using SignContext=SignService.Infrastructure.Data.AppDbContext;
using SignEngine=SignService.Infrastructure.Services.SignService;
using DocEngine=DocumentService.Infrastructure.Services.DocumentService;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var fixture=Path.Combine(Path.GetTempPath(),"hau-pipeline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
var certFolder=Path.Combine(fixture,"certs");
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var word=Word();var originals=new Dictionary<string,byte[]>();var objects=new Dictionary<string,byte[]>();
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["CertificateSettings:CertsDirectory"]=certFolder}).Build();
var manager=Guid.NewGuid();var board=Guid.NewGuid();
try
{
    var certs=new CertificateService(config,NullLogger<CertificateService>.Instance);
    await certs.InitializeRootCaAsync();await certs.IssueCertificateAsync(manager,"fixture-manager","Fixture Manager");await certs.IssueCertificateAsync(board,"fixture-board","Fixture Board");
    var pdf=new PdfSigningService(certs,NullLogger<PdfSigningService>.Instance);
    await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
    await using var docDb=new DocContext(new DbContextOptionsBuilder<DocContext>().UseSqlite(connection).Options);
    await docDb.Database.EnsureCreatedAsync();
    await docDb.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (Id TEXT PRIMARY KEY,DocId TEXT NOT NULL,SignerId TEXT NOT NULL,SignatureType TEXT NOT NULL,SignedAt TEXT NOT NULL,SignedFilePath TEXT NULL)");
    await docDb.Database.ExecuteSqlRawAsync("CREATE TABLE OcrJobs (Id TEXT,DocumentId TEXT,SourcePath TEXT,Status TEXT)");
    await using var signDb=new SignContext(new DbContextOptionsBuilder<SignContext>().UseSqlite(connection).Options);
    var minio=new Mock<IMinioService>();var failUpload=false;
    minio.Setup(m=>m.DownloadFileAsync(It.IsAny<string>())).ReturnsAsync((string path)=>objects[path].ToArray());
    minio.Setup(m=>m.UploadFileAsync(It.IsAny<string>(),It.IsAny<byte[]>(),It.IsAny<string>())).Returns((string path,byte[] content,string type)=>{
        if(failUpload)throw new IOException("Fixture storage failure");objects[path]=content.ToArray();return Task.CompletedTask;});
    var http=new Mock<IHttpClientFactory>();http.Setup(f=>f.CreateClient("signing-word-export")).Returns(()=>new HttpClient(new ExportHandler(word)){BaseAddress=new Uri("http://fixture/")});
    var preparer=new SigningDocumentPreparer(signDb,minio.Object,http.Object);
    var signing=new SignEngine(new SignatureRepository(signDb),certs,pdf,minio.Object,new DocumentFileRepository(signDb),config,NullLogger<SignEngine>.Instance,preparer);
    var files=new Mock<DocumentService.Core.Interfaces.IFileStorageService>();
    var documents=new DocEngine(new DocumentRepository(docDb),new DocumentTypeRepository(docDb),new DocumentProcessRepository(docDb),files.Object,
        Mock.Of<DocumentService.Core.Interfaces.IKafkaProducerService>(),NullLogger<DocEngine>.Instance);
    var results=new Dictionary<string,object>();
    async Task<Guid> Create(string extension,bool ocr=true)
    {
        signDb.ChangeTracker.Clear();docDb.ChangeTracker.Clear();
        var id=Guid.NewGuid();var path="documents/"+id+extension;
        var bytes=extension==".docx"?word:extension==".pdf"?await SigningDocumentPreparer.ConvertWordAsync(word):new byte[]{255,216,255,0};
        objects[path[10..]]=bytes;originals[path[10..]]=bytes.ToArray();
        var raw=ocr?JsonSerializer.Serialize(new{provider="gemini",sourcePath=path,layout=new{blocks=new[]{new{type="paragraph",text="SMOKE CANONICAL OCR CONTENT"}}}}):null;
        docDb.Documents.Add(new(){Id=id,Title="Fixture "+extension,FileName="fixture"+extension,MinioPath=path,Status="PendingDeptReview",OcrDataRaw=raw,
            CreatedById=manager,DocTypeId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),ConcurrencyVersion=Guid.NewGuid()});
        await docDb.SaveChangesAsync();docDb.ChangeTracker.Clear();return id;
    }
    var image=await Create(".jpg");
    await signing.PersonalSignAsync(new(){DocId=image,SignerId=manager});docDb.ChangeTracker.Clear();
    var afterFirst=await documents.DeptSignAsync(image,manager,"First");
    Check(afterFirst.SigningStartedAt.HasValue&&afterFirst.SignedPdfPath!=null,"Missing lock or signed path");
    Check(afterFirst.MinioPath.EndsWith(".jpg"),"Original source changed");
    var firstPath=afterFirst.SignedPdfPath!;
    docDb.ChangeTracker.Clear();await documents.SubmitToDirectorAsync(image,manager,"Forward");signDb.ChangeTracker.Clear();
    await signing.LegalSealAsync(new(){DocId=image,SignerId=board});docDb.ChangeTracker.Clear();
    await documents.DirectorSignAsync(image,board,"Final");
    var verification=await signing.VerifySignaturesAsync(image);Check(verification.IsValid&&verification.TotalSignatures==2,"Two-signature verification failed");
    var signed=(await documents.GetDocumentByIdAsync(image)).SignedPdfPath!;
    using(var content=new PdfDocument(new PdfReader(new MemoryStream(objects[signed]))))
        Check(PdfTextExtractor.GetTextFromPage(content.GetPage(1)).Contains("SMOKE CANONICAL OCR CONTENT"),"Signed PDF does not contain canonical OCR text");
    Check(objects.ContainsKey(firstPath),"First signed version was lost");
    File.WriteAllBytes(Path.Combine(output,"ocr-two-signatures.pdf"),objects[signed]);
    results["image_ocr_to_pdf_two_signatures"]=verification;
    await docDb.Documents.Where(d=>d.Id==image).ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Status,"Rejected"));docDb.ChangeTracker.Clear();
    try { await documents.UpdateOcrDataAsync(image,new UpdateOcrDto{OcrDataRaw="{}"},manager);throw new Exception("Signed OCR was editable"); }
    catch(DocumentService.Core.Exceptions.DocumentConflictException){results["signed_returned_content_locked"]=true;}
    var missing=await Create(".jpg",false);var before=objects.Count;
    try { await signing.PersonalSignAsync(new(){DocId=missing,SignerId=manager});throw new Exception("Unsigned image accepted"); }
    catch(SignService.Core.Exceptions.SignServiceException){ }
    signDb.ChangeTracker.Clear();docDb.ChangeTracker.Clear();
    Check(objects.Count==before&&!(await documents.GetDocumentByIdAsync(missing)).SigningStartedAt.HasValue,"Failed OCR preparation locked or stored a file");
    results["image_without_ocr_blocked_without_mutation"]=true;
    var legacy=await Create(".jpg");var legacyJob=Guid.NewGuid();var legacySource="documents/"+legacy+".jpg";
    var legacyRaw=JsonSerializer.Serialize(new{provider="gemini",jobId=legacyJob,layout=new{blocks=new[]{new{type="paragraph",text="SMOKE CANONICAL OCR CONTENT"}}}});
    await docDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO OcrJobs VALUES ({legacyJob},{legacy},{legacySource},'Succeeded')");
    await docDb.Documents.Where(d=>d.Id==legacy).ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Status,"DeptSigned").SetProperty(d=>d.OcrDataRaw,legacyRaw));
    signDb.ChangeTracker.Clear();docDb.ChangeTracker.Clear();
    await signing.PersonalSignAsync(new(){DocId=legacy,SignerId=manager});docDb.ChangeTracker.Clear();
    var repaired=await documents.DeptSignAsync(legacy,manager,"Fixture reconciliation");
    Check(repaired.Status=="DeptSigned" && repaired.SignedPdfPath!=null && (await signing.VerifySignaturesAsync(legacy)).IsValid,"Legacy unproven status was not reconciled through actual PDF signing");
    results["legacy_ocr_bound_by_successful_job_and_signed_without_rewinding_status"]=true;
    foreach(var extension in new[]{".docx",".pdf"})
    {
        var id=await Create(extension,false);await signing.PersonalSignAsync(new(){DocId=id,SignerId=manager});
        Check((await signing.VerifySignaturesAsync(id)).IsValid,"Direct "+extension+" failed");results["direct_"+extension]=true;
    }
    var incoming=await Create(".jpg");var unit=Guid.NewGuid();var specialist=Guid.NewGuid();
    await docDb.Documents.Where(d=>d.Id==incoming).ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Status,"PendingDirection")
        .SetProperty(d=>d.DocTypeId,Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    signDb.ChangeTracker.Clear();
    var directory=new Mock<IWorkflowDirectory>();
    var users=new Dictionary<Guid,WorkflowUser>{[manager]=new(manager,"Manager",unit,true,["Manager"]),[board]=new(board,"Board",null,true,["BoardOfDirectors"]),[specialist]=new(specialist,"Specialist",unit,true,["Specialist"])};
    directory.Setup(d=>d.GetUserAsync(It.IsAny<Guid>())).ReturnsAsync((Guid user)=>users[user]);
    directory.Setup(d=>d.GetDepartmentsAsync()).ReturnsAsync(new List<WorkflowDepartment>{new(unit,"Fixture unit",null)});
    var workflow=new IncomingWorkflow(signDb,directory.Object,minio.Object,pdf,certs,preparer);
    var incomingVersion=(await documents.GetDocumentByIdAsync(incoming)).ConcurrencyVersion;
    await workflow.DirectAsync(incoming,new(board,["BoardOfDirectors"]),new(){ExpectedVersion=incomingVersion,DepartmentId=unit,Comment="Fixture direction"});
    signDb.ChangeTracker.Clear();docDb.ChangeTracker.Clear();
    incomingVersion=(await documents.GetDocumentByIdAsync(incoming)).ConcurrencyVersion;
    await workflow.AssignAsync(incoming,new(manager,["Manager"]),new(){ExpectedVersion=incomingVersion,ToUserId=specialist,Comment="Fixture assignment"});
    var incomingVerification=await signing.VerifySignaturesAsync(incoming);
    Check(incomingVerification.IsValid&&incomingVerification.TotalSignatures==2,"Incoming OCR to two signatures failed");
    results["incoming_image_ocr_direction_and_assignment"]=incomingVerification;
    var failure=await Create(".pdf",false);failUpload=true;
    try { await signing.PersonalSignAsync(new(){DocId=failure,SignerId=manager});throw new Exception("Storage failure ignored"); }
    catch(IOException){ }
    failUpload=false;signDb.ChangeTracker.Clear();docDb.ChangeTracker.Clear();
    var failureDoc=await documents.GetDocumentByIdAsync(failure);
    Check(failureDoc.SigningStartedAt==null&&failureDoc.SignedPdfPath==null&&!await signDb.Signatures.AnyAsync(s=>s.DocId==failure),"Failure did not roll back signing state");
    results["storage_failure_rolls_back_and_keeps_unsigned_source"]=true;
    Check(originals.All(pair=>objects[pair.Key].SequenceEqual(pair.Value)),"Original source bytes changed");results["all_original_source_bytes_preserved"]=true;
    File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("PASS: canonical OCR conversion, two real signatures, direct Word/PDF, locks, rollback, preserved sources.");
}
finally{Directory.Delete(fixture,true);}

static byte[] Word()
{
    using var buffer=new MemoryStream();
    using(var archive=new ZipArchive(buffer,ZipArchiveMode.Create,true))
    {
        void Write(string path,string text){using var writer=new StreamWriter(archive.CreateEntry(path).Open());writer.Write(text);}
        Write("[Content_Types].xml","""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
        Write("_rels/.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
        Write("word/document.xml","""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>SMOKE CANONICAL OCR CONTENT</w:t></w:r></w:p><w:sectPr/></w:body></w:document>""");
    }
    return buffer.ToArray();
}
sealed class ExportHandler(byte[] content):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        =>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(content)});
}
