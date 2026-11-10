using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public class DocumentFileRepositoryTests
{
    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.PendingDeptReview)]
    [InlineData(DocumentStatus.DirectorSigned)]
    public async Task UploadCannotCommitAcrossMetadataOrWorkflowChange(string nextStatus)
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();var id=Guid.NewGuid();var version=Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT, SignerId TEXT, SignedAt TEXT, SignatureType TEXT, SignedFilePath TEXT)");
        db.Add(new Document{Id=id,Title="Draft",DocTypeId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),MinioPath="documents/original.pdf",ConcurrencyVersion=version});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var changedVersion=Guid.NewGuid();
        await db.Documents.Where(d=>d.Id==id).ExecuteUpdateAsync(u=>u.SetProperty(d=>d.Status,nextStatus).SetProperty(d=>d.ConcurrencyVersion,changedVersion));
        var repository=new DocumentRepository(db);
        Assert.False(await repository.ReplaceFileAsync(id,"documents/original.pdf","documents/new.pdf","new.pdf",version));
        Assert.Equal("documents/original.pdf",(await repository.GetByIdAsync(id))!.MinioPath);
    }

    [Fact]
    public async Task ReplaceFile_RejectsStalePath_AndMetadataEditCannotRestoreDeletedPdf()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Signatures (DocId TEXT, SignerId TEXT, SignedAt TEXT, SignatureType TEXT, SignedFilePath TEXT)");
        var id = Guid.NewGuid();
        context.Documents.Add(new Document
        {
            Id = id, Title = "Original", MinioPath = "documents/old.pdf", FileName = "old.pdf",
            DocTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new DocumentRepository(context);
        var staleMetadata = (await repository.GetByIdAsync(id))!;

        (await repository.ReplaceFileAsync(id, "documents/old.pdf", "documents/new.pdf", "Công văn mới.pdf", staleMetadata.ConcurrencyVersion))
            .Should().BeTrue();
        (await repository.ReplaceFileAsync(id, "documents/old.pdf", "documents/rejected.pdf", "rejected.pdf", staleMetadata.ConcurrencyVersion))
            .Should().BeFalse();
        (await repository.ReplaceFileAsync(Guid.NewGuid(), "", "documents/rejected.pdf", "rejected.pdf", staleMetadata.ConcurrencyVersion))
            .Should().BeFalse();
        staleMetadata.Title = "Updated metadata";
        await FluentActions.Invoking(() => repository.UpdateAsync(staleMetadata))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        context.ChangeTracker.Clear();
        var freshMetadata = (await repository.GetByIdAsync(id))!;
        freshMetadata.Title = "Updated metadata";
        await repository.UpdateAsync(freshMetadata);
        context.ChangeTracker.Clear();
        var saved = (await repository.GetByIdAsync(id))!;
        saved.Title.Should().Be("Updated metadata");
        saved.MinioPath.Should().Be("documents/new.pdf");
        saved.FileName.Should().Be("Công văn mới.pdf");
        (await repository.ReplaceFileAsync(id, saved.MinioPath, "documents/last.pdf", "last.pdf", saved.ConcurrencyVersion)).Should().BeTrue();
        var last = (await repository.GetByIdAsync(id))!;
        last.FileName.Should().Be("last.pdf");
        last.Title.Should().Be("Updated metadata");
    }
}
