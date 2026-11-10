using System.Data.Common;
using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.API.Filters;

// Batch creator and history actor names from the shared Identity tables.
public sealed class DocumentCreatorFilter(AppDbContext db, ILogger<DocumentCreatorFilter> logger) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var documents = (context.Result as ObjectResult)?.Value switch
        {
            ApiResponse<DocumentDto> { Data: { } document } => new[] { document },
            ApiResponse<PagedResult<DocumentDto>> { Data: { } page } => page.Items.ToArray(),
            _ => Array.Empty<DocumentDto>()
        };
        var ids = documents.Where(d => d.CreatedById.HasValue && d.CreatedById != Guid.Empty)
            .Select(d => d.CreatedById!.Value)
            .Concat(documents.SelectMany(d => d.Processes).Select(p => p.FromUserId))
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var document in documents)
            document.CanReview = document.DocTypeName != "CongVanDen" && db.AccessScope?.CanReview(document.DepartmentId) == true;
        if (db.AccessScope is { } scope && documents.Length > 0)
        {
            var docIds = documents.Select(d => d.Id).ToArray();
            var signed = await db.SignatureEvidence.Where(s => s.SignerId == scope.UserId && docIds.Contains(s.DocId))
                .GroupBy(s => s.DocId).Select(g => new { Id = g.Key, At = g.Max(s => s.SignedAt) }).ToDictionaryAsync(s => s.Id, s => s.At);
            foreach (var document in documents)
                if (signed.TryGetValue(document.Id, out var at)) document.MySignedAt = at;
        }
        if (ids.Length > 0)
        {
            try
            {
                var names = await db.Database.SqlQueryRaw<CreatorNameRow>("""
                    SELECT "Id", COALESCE(NULLIF(TRIM("FullName"), ''), NULLIF(TRIM("Username"), '')) AS "Name" FROM "AppUsers"
                    """).Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, context.HttpContext.RequestAborted);
                foreach (var document in documents)
                {
                    if (document.CreatedById is Guid id && names.TryGetValue(id, out var name)) document.CreatedByName = name;
                    foreach (var process in document.Processes)
                        if (names.TryGetValue(process.FromUserId, out var actorName)) process.ProcessedByName = actorName;
                }
            }
            catch (DbException ex)
            {
                // A metadata lookup must not turn an already committed create/update into a failed action.
                logger.LogWarning("Cannot resolve document creator names ({ErrorType})", ex.GetType().Name);
            }
        }
        await next();
    }

    public sealed class CreatorNameRow
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
    }
}
