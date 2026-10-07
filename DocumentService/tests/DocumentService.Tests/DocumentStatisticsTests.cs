using DocumentService.Core.Entities;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Tests;

public class DocumentStatisticsTests
{
    private static readonly DateTime Start = new(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task EmptyDatabase_ReturnsGenuineZeros()
    {
        await using var context = CreateContext();
        var result = await new DocumentRepository(context).GetStatisticsAsync(Owner, Start, Start.AddDays(1));
        result.TotalDocuments.Should().Be(0);
        result.TodayDocuments.Should().Be(0);
        result.AssignedDocuments.Should().Be(0);
    }

    [Fact]
    public async Task CountsAllStatusesAndOcrSeparatelyFromWorkflow()
    {
        await using var context = CreateContext();
        foreach (var status in new[] { DocumentStatus.Draft, DocumentStatus.PendingDeptReview, DocumentStatus.DeptSigned,
            DocumentStatus.PendingDirectorSign, DocumentStatus.DirectorSigned, DocumentStatus.Published, DocumentStatus.Rejected })
            context.Documents.Add(Document(status, Owner, Start.AddHours(1)));
        context.Documents.Local.First().MinioPath = "documents/test.pdf";
        context.Documents.Local.Last().MinioPath = "documents/recognized.pdf";
        context.Documents.Local.Last().OcrDataRaw = "{}";
        await context.SaveChangesAsync();
        var result = await new DocumentRepository(context).GetStatisticsAsync(Owner, Start, Start.AddDays(1));
        result.TotalDocuments.Should().Be(7);
        result.TodayDocuments.Should().Be(7);
        result.PendingDocuments.Should().Be(4);
        result.PendingDeptDocuments.Should().Be(1);
        result.PendingDirectorDocuments.Should().Be(1);
        result.PublishedDocuments.Should().Be(1);
        result.DirectorSignedDocuments.Should().Be(2);
        result.PendingOcrDocuments.Should().Be(1);
        result.MyDraftDocuments.Should().Be(1);
        result.MyPendingDocuments.Should().Be(3);
    }

    [Fact]
    public async Task TodayUsesVietnamMidnightAndOriginalCreation_NotIssuedDateOrResubmission()
    {
        await using var context = CreateContext();
        var yesterday = Document(DocumentStatus.Draft, Owner, Start.AddTicks(-1));
        yesterday.IssuedDate = DateOnly.FromDateTime(Start.AddDays(1));
        yesterday.Processes.Add(Process(yesterday.Id, DocumentAction.Submit, Other, Start.AddHours(2)));
        context.Documents.AddRange(yesterday, Document(DocumentStatus.Draft, Owner, Start),
            Document(DocumentStatus.Draft, Owner, Start.AddDays(1).AddTicks(-1)),
            Document(DocumentStatus.Draft, Owner, Start.AddDays(1)));
        context.Documents.Add(new Document { Id = Guid.NewGuid(), Title = "Legacy without process" });
        await context.SaveChangesAsync();
        var result = await new DocumentRepository(context).GetStatisticsAsync(Owner, Start, Start.AddDays(1));
        result.TotalDocuments.Should().Be(5);
        result.TodayDocuments.Should().Be(2);
        result.MyDraftDocuments.Should().Be(4);
    }

    [Fact]
    public async Task PersonalCountsUseCreatorAndDistinctDocumentsAssignedByActor()
    {
        await using var context = CreateContext();
        var mine = Document(DocumentStatus.Draft, Owner, Start);
        var theirs = Document(DocumentStatus.PendingDeptReview, Other, Start);
        theirs.Processes.Add(Process(theirs.Id, DocumentAction.Submit, Owner, Start.AddHours(1)));
        theirs.Processes.Add(Process(theirs.Id, DocumentAction.Assign, Owner, Start.AddHours(2)));
        theirs.Processes.Add(Process(theirs.Id, DocumentAction.Assign, Owner, Start.AddHours(3)));
        mine.Processes.Add(Process(mine.Id, DocumentAction.Assign, Other, Start.AddHours(2)));
        context.Documents.AddRange(mine, theirs);
        await context.SaveChangesAsync();
        var result = await new DocumentRepository(context).GetStatisticsAsync(Owner, Start, Start.AddDays(1));
        result.MyDraftDocuments.Should().Be(1);
        result.MyPendingDocuments.Should().Be(0);
        result.AssignedDocuments.Should().Be(1);
        var otherResult = await new DocumentRepository(context).GetStatisticsAsync(Other, Start, Start.AddDays(1));
        otherResult.MyDraftDocuments.Should().Be(0);
        otherResult.MyPendingDocuments.Should().Be(1);
        otherResult.AssignedDocuments.Should().Be(1);
    }

    private static Document Document(string status, Guid owner, DateTime timestamp)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "Statistics fixture", Status = status };
        doc.Processes.Add(Process(doc.Id, DocumentAction.Submit, owner, timestamp));
        return doc;
    }
    private static DocumentProcess Process(Guid doc, string action, Guid actor, DateTime timestamp)
        => new() { Id = Guid.NewGuid(), DocId = doc, Action = action, FromUserId = actor, Timestamp = timestamp };
}
