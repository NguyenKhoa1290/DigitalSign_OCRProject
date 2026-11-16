using System.Security.Cryptography;
using System.Text;
using DocumentService.API.Controllers;
using DocumentService.API.Services;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Npgsql;

namespace DocumentService.Tests;

public sealed class PublicLinksPostgresFactAttribute : FactAttribute
{
    public PublicLinksPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HAU_PUBLIC_LINK_TEST_PG")))
            Skip = "Requires a disposable public_link_tests PostgreSQL database.";
    }
}

public class PublicDocumentLinksTests
{
    [PublicLinksPostgresFact]
    public async Task OnlyPublishedSignedVersionCanBeSharedAndLinksRotateAndRevoke()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HAU_PUBLIC_LINK_TEST_PG"));
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.NotEqual(5432, settings.Port);
        Assert.Equal("public_link_tests", settings.Database);
        var schema = "share_" + Guid.NewGuid().ToString("N");
        await using var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        await using (var create = admin.CreateCommand($"CREATE SCHEMA {schema}")) await create.ExecuteNonQueryAsync();
        settings.SearchPath = schema;
        try
        {
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            var id = Guid.NewGuid();
            await using (var create = source.CreateCommand("""
                CREATE TABLE "Documents" ("Id" uuid PRIMARY KEY, "Status" text, "IsDeleted" boolean,
                    "SignedPdfPath" text, "FileName" text);
                CREATE TABLE "Signatures" ("DocId" uuid, "SignatureType" text, "SignedFilePath" text);
                INSERT INTO "Documents" VALUES (@id,'Published',false,'signed.pdf','Văn bản.docx');
                INSERT INTO "Signatures" VALUES (@id,'LegalSeal','signed.pdf');
                """))
            {
                create.Parameters.AddWithValue("id", id);
                await create.ExecuteNonQueryAsync();
            }
            await PublicDocumentLinks.InitializeAsync(source);
            var links = new PublicDocumentLinks(source);
            var first = await links.CreateAsync(id, Guid.NewGuid(), default);
            var firstKey = first.Split("?key=")[1];
            var file = await links.FindAsync(id, firstKey, default);
            Assert.Equal("signed.pdf", file!.Value.Path);
            Assert.Equal("Văn bản.pdf", file.Value.FileName);
            var storage = new Mock<IFileStorageService>();
            storage.Setup(service => service.DownloadFileAsync("signed.pdf", "documents"))
                .ReturnsAsync(() => new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 fixture")));
            var documents = new Mock<IDocumentService>();
            var controller = new PublicDocumentsController(links, documents.Object, storage.Object)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            Assert.False(controller.User.Identity?.IsAuthenticated ?? false);
            var preview = Assert.IsType<FileStreamResult>(await controller.Read(id, firstKey, false, default));
            Assert.Equal("application/pdf", preview.ContentType);
            Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
            Assert.Contains("inline", controller.Response.Headers.ContentDisposition.ToString());
            await preview.FileStream.DisposeAsync();
            var download = Assert.IsType<FileStreamResult>(await controller.Read(id, firstKey, true, default));
            Assert.Equal("Văn bản.pdf", download.FileDownloadName);
            await download.FileStream.DisposeAsync();
            Assert.IsType<NotFoundResult>(await controller.Read(id, "bad-key", false, default));
            documents.Setup(service => service.GetDocumentByIdAsync(id))
                .ThrowsAsync(new UnauthorizedDocumentAccessException());
            await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(() => controller.Create(id, default));
            Assert.NotNull(await links.FindAsync(id, firstKey, default));
            Assert.Null(await links.FindAsync(id, null, default));
            Assert.Null(await links.FindAsync(id, new string('0', 64), default));
            Assert.Null(await links.FindAsync(Guid.NewGuid(), firstKey, default));
            await using (var stored = source.CreateCommand("SELECT \"TokenHash\" FROM \"PublicDocumentLinks\""))
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstKey))), await stored.ExecuteScalarAsync());

            var secondKey = (await links.CreateAsync(id, Guid.NewGuid(), default)).Split("?key=")[1];
            Assert.Null(await links.FindAsync(id, firstKey, default));
            Assert.NotNull(await links.FindAsync(id, secondKey, default));
            foreach (var change in new[] {
                "UPDATE \"Documents\" SET \"Status\"='Draft'",
                "UPDATE \"Documents\" SET \"IsDeleted\"=true",
                "UPDATE \"Documents\" SET \"SignedPdfPath\"='different.pdf'",
                "DELETE FROM \"Signatures\"" })
            {
                await using (var command = source.CreateCommand(change)) await command.ExecuteNonQueryAsync();
                Assert.Null(await links.FindAsync(id, secondKey, default));
                await Assert.ThrowsAsync<DocumentConflictException>(() => links.CreateAsync(id, Guid.NewGuid(), default));
                await using var restore = source.CreateCommand("""
                    UPDATE "Documents" SET "Status"='Published', "IsDeleted"=false,"SignedPdfPath"='signed.pdf';
                    DELETE FROM "Signatures";
                    INSERT INTO "Signatures" VALUES (@id,'LegalSeal','signed.pdf');
                    """);
                restore.Parameters.AddWithValue("id", id);
                await restore.ExecuteNonQueryAsync();
            }
            await links.RevokeAsync(id, default);
            await links.RevokeAsync(id, default);
            Assert.Null(await links.FindAsync(id, secondKey, default));
            var thirdKey = (await links.CreateAsync(id, Guid.NewGuid(), default)).Split("?key=")[1];
            await using (var delete = source.CreateCommand("DELETE FROM \"Documents\"")) await delete.ExecuteNonQueryAsync();
            Assert.Null(await links.FindAsync(id, thirdKey, default));
        }
        finally
        {
            await using var cleanup = admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void OnlyClerkAndAdminManageLinksAndOnlyReadEndpointIsAnonymous()
    {
        var type = typeof(PublicDocumentsController);
        foreach (var name in new[] { "Create", "Revoke" })
        {
            var method = type.GetMethod(name)!;
            var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), false));
            Assert.Equal("Clerk,Admin", ((AuthorizeAttribute)authorization).Roles);
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
        }
        Assert.Single(type.GetMethod("Read")!.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
    }
}
