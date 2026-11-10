using DocumentService.API.Filters;
using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace DocumentService.Tests;

public class DocumentCreatorFilterTests
{
    private static async Task Apply(AppDbContext db, object body, int status = 200)
    {
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var result = new ObjectResult(body) { StatusCode = status };
        var filters = new List<IFilterMetadata>(); var controller = new object(); var called = false;
        var context = new ResultExecutingContext(action, filters, result, controller);
        await new DocumentCreatorFilter(db, NullLogger<DocumentCreatorFilter>.Instance).OnResultExecutionAsync(context, () =>
        {
            called = true; return Task.FromResult(new ResultExecutedContext(action, filters, result, controller));
        });
        Assert.True(called); Assert.Equal(status, result.StatusCode);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public async Task DetailAndCreationResponsesResolveExistingCreator_WithoutChangingOwnership(int status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, FullName TEXT NULL, Username TEXT NULL)");
        var user = Guid.NewGuid(); var name = "Trần Thanh Bình";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppUsers VALUES ({user},{name},'fixture')");
        var dto = new DocumentDto { Id = Guid.NewGuid(), CreatedById = user };
        await Apply(db, ApiResponse<DocumentDto>.Ok(dto), status);
        Assert.Equal(name, dto.CreatedByName); Assert.Equal(user, dto.CreatedById);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PagedResponsesBatchNames_UseUsernameWhenNameBlank_AndKeepMissingUserUnknown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var counter = new LookupCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(counter).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, FullName TEXT NULL, Username TEXT NULL)");
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var missing = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppUsers VALUES ({a},'  Admin fixture  ','admin'),({b},'  ','GV001')");
        var docs = new[] { new DocumentDto { CreatedById = a }, new DocumentDto { CreatedById = a },
            new DocumentDto { CreatedById = b }, new DocumentDto { CreatedById = missing }, new DocumentDto() };
        await Apply(db, ApiResponse<PagedResult<DocumentDto>>.Ok(new(docs, docs.Length, 1, 15)));
        Assert.Equal(new string?[] { "Admin fixture", "Admin fixture", "GV001", null, null }, docs.Select(d => d.CreatedByName));
        Assert.Equal(missing, docs[3].CreatedById); Assert.Null(docs[4].CreatedById);
        Assert.Equal(1, counter.Lookups); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task HistoryResolvesEditorsWithoutRequiringCreatorAndUsesOneLookup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var counter = new LookupCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(counter).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, FullName TEXT NULL, Username TEXT NULL)");
        var editor = Guid.NewGuid(); var other = Guid.NewGuid(); var missing = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppUsers VALUES ({editor},'  Văn thư hiệu chỉnh  ','VT001'),({other},NULL,'GV002')");
        var dto = new DocumentDto { Processes = new()
        {
            new() { FromUserId = editor, Action = "OcrEdited", Comment = "Lần 1: ban-sua.docx" },
            new() { FromUserId = editor, Action = "OcrEdited", Comment = "Lần 2: ban-sua-2.docx" },
            new() { FromUserId = other, Action = "Update" },
            new() { FromUserId = missing, Action = "Upload" }
        } };
        await Apply(db, ApiResponse<DocumentDto>.Ok(dto));
        Assert.Equal(new string?[] { "Văn thư hiệu chỉnh", "Văn thư hiệu chỉnh", "GV002", null }, dto.Processes.Select(p => p.ProcessedByName));
        Assert.Contains("ban-sua-2.docx", dto.Processes[1].Comment);
        Assert.Equal(missing, dto.Processes[3].FromUserId);
        Assert.Equal(1, counter.Lookups); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MissingIdentityTableDoesNotFailAnAlreadyCommittedCreation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var dto = new DocumentDto { CreatedById = Guid.NewGuid() };
        await Apply(db, ApiResponse<DocumentDto>.Ok(dto), 201);
        Assert.Null(dto.CreatedByName);
    }

    private sealed class LookupCounter : DbCommandInterceptor
    {
        public int Lookups;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("AppUsers")) Lookups++;
            return base.ReaderExecutingAsync(command, data, result, cancellationToken);
        }
    }
}
