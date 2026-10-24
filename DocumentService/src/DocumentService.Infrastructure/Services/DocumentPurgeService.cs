using System.Text.Json;
using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentService.Infrastructure.Services;

public class DocumentPurgeService(AppDbContext db, IFileStorageService storage, ILogger<DocumentPurgeService> logger) : IDocumentPurgeService
{
    public async Task<DocumentPurgeResult> PurgeAsync(Guid id, DocumentActor actor, DocumentPurgeDto request)
    {
        if (!actor.IsAdmin || actor.UserId == Guid.Empty) throw new UnauthorizedDocumentAccessException();
        if (!request.ExpectedVersion.HasValue || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || string.IsNullOrWhiteSpace(request.ConfirmationTitle) || request.ConfirmationTitle.Length > 500)
            throw new DocumentServiceException("Nhập lý do, tên xác nhận và phiên bản công văn hợp lệ.");
        Guid recordId;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\"={id} FOR UPDATE");
            var document = await db.Documents.IgnoreQueryFilters().Include(d => d.Processes).SingleOrDefaultAsync(d => d.Id == id);
            if (document == null)
            {
                var previous = await db.DocumentPurgeRecords.SingleOrDefaultAsync(p => p.DocId == id) ?? throw new DocumentNotFoundException(id);
                Confirm(previous.Title, previous.OriginalVersion, request);
                recordId = previous.Id; // A retry after an uncertain commit must not duplicate the audit.
            }
            else
            {
                if (!document.IsDeleted) throw new DocumentConflictException("Chỉ xóa vĩnh viễn công văn đã xóa hoặc hủy. Hãy tải lại danh sách.");
                Confirm(document.Title, document.ConcurrencyVersion, request);
                var names = new[] { document.MinioPath, document.IncomingOriginalPath, document.IncomingDirectionPath }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => ObjectName(p!)).Distinct(StringComparer.Ordinal).ToArray();
                var record = new DocumentPurgeRecord { DocId = id, Title = document.Title, OriginalStatus = document.Status,
                    OriginalVersion = document.ConcurrencyVersion, ActorId = actor.UserId, Reason = request.Reason.Trim(), PurgedAt = DateTime.UtcNow,
                    ObjectNamesJson = JsonSerializer.Serialize(names) };
                db.DocumentPurgeRecords.Add(record);
                // Signatures are owned by SignService and have no document FK. Delete them in this same transaction.
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Signatures\" WHERE \"DocId\"={id}");
                db.Documents.Remove(document); // Cascades processes and FK-linked notifications, but not the independent purge audit.
                await db.SaveChangesAsync();
                recordId = record.Id;
            }
            await transaction.CommitAsync();
        }
        bool cleaned;
        try { cleaned = await CleanupOneAsync(recordId); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Purge {DocumentId} committed; file cleanup will be retried", id);
            cleaned = false;
        }
        return new(id, !cleaned);
    }

    private static void Confirm(string title, Guid version, DocumentPurgeDto request)
    {
        if (!string.Equals(title.Trim(), request.ConfirmationTitle.Trim(), StringComparison.Ordinal))
            throw new DocumentServiceException("Tên xác nhận chưa khớp với công văn.");
        if (request.ExpectedVersion != version)
            throw new DocumentConflictException("Công văn đã thay đổi. Hãy tải lại danh sách trước khi xóa vĩnh viễn.");
    }

    private static string ObjectName(string path)
    {
        var name = path.Trim().Replace('\\', '/').TrimStart('/');
        return name.StartsWith("documents/", StringComparison.Ordinal) ? name[10..] : name;
    }

    public async Task CleanupPendingAsync(CancellationToken cancellationToken = default)
    {
        var ids = await db.DocumentPurgeRecords.AsNoTracking().Where(p => p.FilesCleanedAt == null)
            .OrderBy(p => p.PurgedAt).Select(p => p.Id).Take(20).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await CleanupOneAsync(id); }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Cannot clean files for purge record {PurgeId}; will retry", id); }
        }
    }

    private async Task<bool> CleanupOneAsync(Guid id)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"DocumentPurgeRecords\" WHERE \"Id\"={id} FOR UPDATE");
        var job = await db.DocumentPurgeRecords.SingleAsync(p => p.Id == id);
        if (job.FilesCleanedAt.HasValue) return true;
        var references = await db.Documents.IgnoreQueryFilters().AsNoTracking()
            .Select(d => new { d.MinioPath, d.IncomingOriginalPath, d.IncomingDirectionPath }).ToListAsync();
        var shared = references.SelectMany(d => new[] { d.MinioPath, d.IncomingOriginalPath, d.IncomingDirectionPath })
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => ObjectName(p!)).ToHashSet(StringComparer.Ordinal);
        var success = true;
        foreach (var name in JsonSerializer.Deserialize<string[]>(job.ObjectNamesJson) ?? [])
        {
            if (string.IsNullOrEmpty(name) || shared.Contains(name)) continue;
            try { success &= await storage.DeleteFileAsync(name, "documents"); }
            catch (Exception) { success = false; }
        }
        job.CleanupAttempts++;
        job.CleanupError = success ? null : "StorageCleanupUnavailable";
        if (success) job.FilesCleanedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return success;
    }
}
