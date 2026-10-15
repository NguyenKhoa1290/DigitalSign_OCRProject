using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public class DocumentOrderingTests
{
    [Fact]
    public async Task CreationOrderIsNewestFirst_StableAcrossPages_AndUnknownDatesLast()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var start = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
        var newerA = MakeDocument(1, "A", start, new DateOnly(2025, 1, 1));
        var newerB = MakeDocument(2, "Z", start, null);
        var older = MakeDocument(3, "Newest issue date", start.AddDays(-1), new DateOnly(2027, 1, 1));
        older.Processes.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = older.Id,
            Action = DocumentAction.Submit, Timestamp = start.AddDays(1) });
        var unknown = new Document { Id = new Guid("00000000-0000-0000-0000-000000000004"), Title = "Unknown creation" };
        var typeId = Guid.NewGuid();
        context.DocumentTypes.Add(new DocumentType { Id = typeId, TypeName = "CongVanDen" });
        foreach (var document in new[] { newerA, newerB, older, unknown }) document.DocTypeId = typeId;
        context.Documents.AddRange(unknown, older, newerB, newerA);
        await context.SaveChangesAsync();
        var repository = new DocumentRepository(context);

        var first = await repository.GetAllAsync(new DocumentQueryParams { Page = 1, PageSize = 2 });
        var second = await repository.GetAllAsync(new DocumentQueryParams { Page = 2, PageSize = 2 });
        first.Total.Should().Be(4); second.Total.Should().Be(4);
        first.Documents.Select(document => document.Id).Should().Equal(newerA.Id, newerB.Id);
        second.Documents.Select(document => document.Id).Should().Equal(older.Id, unknown.Id);
        var repeat = await repository.GetAllAsync(new DocumentQueryParams { Page = 1, PageSize = 2 });
        repeat.Documents.Select(document => document.Id).Should().Equal(newerA.Id, newerB.Id);
        var filtered = await repository.GetAllAsync(new DocumentQueryParams { Search = "Newest issue" });
        filtered.Total.Should().Be(1); filtered.Documents.Single().Id.Should().Be(older.Id);
    }

    private static Document MakeDocument(int number, string title, DateTime created, DateOnly? issued)
    {
        var document = new Document { Id = new Guid($"00000000-0000-0000-0000-{number:000000000000}"),
            Title = title, IssuedDate = issued };
        document.Processes.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = document.Id,
            Action = DocumentAction.Submit, Timestamp = created });
        return document;
    }
}
