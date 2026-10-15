using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public class DocumentFileRepositoryTests
{
    [Fact]
    public async Task ReplaceFile_RejectsStalePath_AndMetadataEditCannotRestoreDeletedPdf()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
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

        (await repository.ReplaceFileAsync(id, "documents/old.pdf", "documents/new.pdf", "Công văn mới.pdf"))
            .Should().BeTrue();
        (await repository.ReplaceFileAsync(id, "documents/old.pdf", "documents/rejected.pdf", "rejected.pdf"))
            .Should().BeFalse();
        (await repository.ReplaceFileAsync(Guid.NewGuid(), "", "documents/rejected.pdf", "rejected.pdf"))
            .Should().BeFalse();
        staleMetadata.Title = "Updated metadata";
        await repository.UpdateAsync(staleMetadata);
        context.ChangeTracker.Clear();
        var saved = (await repository.GetByIdAsync(id))!;
        saved.Title.Should().Be("Updated metadata");
        saved.MinioPath.Should().Be("documents/new.pdf");
        saved.FileName.Should().Be("Công văn mới.pdf");
        (await repository.ReplaceFileAsync(id, saved.MinioPath, "documents/last.pdf", "last.pdf")).Should().BeTrue();
        var last = (await repository.GetByIdAsync(id))!;
        last.FileName.Should().Be("last.pdf");
        last.Title.Should().Be("Updated metadata");
    }
}
