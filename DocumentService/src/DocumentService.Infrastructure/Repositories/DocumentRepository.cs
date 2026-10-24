using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using DocumentService.Core.Exceptions;
using System.Diagnostics;

namespace DocumentService.Infrastructure.Repositories;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _context;

    public DocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Document?> GetByIdAsync(Guid id, bool includeProcesses = false)
    {
        IQueryable<Document> query = _context.Documents
            .AsNoTracking()
            .Include(d => d.DocType);

        if (includeProcesses)
        {
            query = query.Include(d => d.Processes.OrderBy(p => p.Timestamp));
        }

        return await query.FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Document?> GetByDocNumberAsync(string docNumber)
    {
        return await _context.Documents
            .AsNoTracking()
            .Include(d => d.DocType)
            .Include(d => d.Processes.OrderBy(p => p.Timestamp))
            .FirstOrDefaultAsync(d => d.DocNumber == docNumber);
    }

    public async Task<(IEnumerable<Document> Documents, int Total)> GetAllAsync(DocumentQueryParams query)
    {
        var q = _context.Documents
            .AsNoTracking()
            .Include(d => d.DocType)
            .Include(d => d.Processes.OrderBy(process => process.Timestamp))
            .AsQueryable();

        // Search theo Title hoặc DocNumber
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            q = q.Where(d =>
                d.Title.ToLower().Contains(search) ||
                (d.DocNumber != null && d.DocNumber.ToLower().Contains(search)));
        }

        if (query.DocTypeId.HasValue)
            q = q.Where(d => d.DocTypeId == query.DocTypeId.Value);

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(d => d.Status == query.Status);

        if (query.FromDate.HasValue)
            q = q.Where(d => d.IssuedDate.HasValue &&
                d.IssuedDate.Value >= DateOnly.FromDateTime(query.FromDate.Value));

        if (query.ToDate.HasValue)
            q = q.Where(d => d.IssuedDate.HasValue &&
                d.IssuedDate.Value <= DateOnly.FromDateTime(query.ToDate.Value));

        var total = await q.CountAsync();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var documents = await q
            .OrderByDescending(d => d.CreatedAt.HasValue || d.Processes.Any(process => process.Action == DocumentAction.Submit || process.Action == DocumentAction.Create))
            .ThenByDescending(d => d.CreatedAt ?? d.Processes.Where(process => process.Action == DocumentAction.Submit || process.Action == DocumentAction.Create)
                .Select(process => (DateTime?)process.Timestamp).Min())
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (documents, total);
    }

    public async Task<Document> CreateAsync(Document document)
    {
        document.Id = Guid.NewGuid();
        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<Document> UpdateAsync(Document document)
    {
        var expectedVersion = document.ConcurrencyVersion;
        document.ConcurrencyVersion = Guid.NewGuid();
        if (document.Status != DocumentStatus.Draft)
            document.FirstSubmittedAt ??= DateTime.UtcNow;
        _context.Documents.Update(document);
        _context.Entry(document).Property(d => d.ConcurrencyVersion).OriginalValue = expectedVersion;
        if (document.Status == DocumentStatus.Draft)
            _context.Entry(document).Property(d => d.FirstSubmittedAt).IsModified = false;
        foreach (var property in new[] { nameof(Document.CreatedById), nameof(Document.CreatedAt),
            nameof(Document.DeletionEligibilityConfirmed), nameof(Document.FirstAssignedAt),
            nameof(Document.ProcessingDepartmentId), nameof(Document.ProcessingDepartmentName),
            nameof(Document.AssignedToId), nameof(Document.AssignedToName),
            nameof(Document.IncomingOriginalPath), nameof(Document.IncomingDirectionPath),
            nameof(Document.OfficiallyReceivedAt), nameof(Document.SigningStartedAt), nameof(Document.IsDeleted),
            nameof(Document.DeletedAt), nameof(Document.DeletedById), nameof(Document.DeleteReason) })
            _context.Entry(document).Property(property).IsModified = false;
        // File references are changed only by ReplaceFileAsync. A stale OCR/edit request
        // must not restore a PDF that has already been replaced and deleted.
        _context.Entry(document).Property(d => d.MinioPath).IsModified = false;
        _context.Entry(document).Property(d => d.FileName).IsModified = false;
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<bool> ReplaceFileAsync(Guid id, string expectedPath, string newPath, string fileName)
    {
        var version = Guid.NewGuid();
        // Compare the stored path so simultaneous uploads cannot delete the wrong PDF.
        var changed = await _context.Documents
            .Where(document => document.Id == id && document.MinioPath == expectedPath)
            .ExecuteUpdateAsync(update => update
                .SetProperty(document => document.MinioPath, newPath)
                .SetProperty(document => document.FileName, fileName)
                .SetProperty(document => document.ConcurrencyVersion, version));
        return changed == 1;
    }

    public Task<Document?> GetIncludingDeletedAsync(Guid id) => _context.Documents.IgnoreQueryFilters()
        .AsNoTracking().Include(d => d.DocType).Include(d => d.Processes)
        .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<(IEnumerable<Document> Documents, int Total)> GetDeletedAsync(DocumentQueryParams query)
    {
        var q = _context.Documents.IgnoreQueryFilters().AsNoTracking().Where(d => d.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            q = q.Where(d => d.Title.ToLower().Contains(search) || (d.DocNumber != null && d.DocNumber.ToLower().Contains(search)));
        }
        var total = await q.CountAsync();
        var items = await q.Include(d => d.DocType).Include(d => d.Processes)
            .OrderByDescending(d => d.DeletedAt).ThenBy(d => d.Id)
            .Skip((Math.Max(query.Page, 1) - 1) * Math.Clamp(query.PageSize, 1, 100))
            .Take(Math.Clamp(query.PageSize, 1, 100)).ToListAsync();
        return (items, total);
    }

    private async Task LockAsync(Guid id)
    {
        // All services use the same PostgreSQL row lock for deletion, assignment and signing.
        if (_context.Database.IsNpgsql())
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\"={id} FOR UPDATE");
    }

    public async Task SetDeletedAsync(Guid id, DocumentActor actor, string? reason, bool deleted)
    {
        actor.RequireDeleteRole();
        if (!deleted && !actor.IsAdmin) throw new UnauthorizedDocumentAccessException();
        reason = reason?.Trim();
        if (reason?.Length > 1000 || (actor.IsAdmin && string.IsNullOrWhiteSpace(reason)))
            throw new DocumentServiceException("Admin phải nhập lý do, tối đa 1000 ký tự.");
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockAsync(id);
        var document = await GetIncludingDeletedAsync(id) ?? throw new DocumentNotFoundException(id);
        DocumentDeletionPolicy.Authorize(document, actor, restore: !deleted);
        // Authorize before acknowledging a repeated request. No duplicate audit events.
        if (document.IsDeleted == deleted) return;
        DocumentDeletionPolicy.RequireEligible(document);
        if (await _context.SignatureEvidence.AnyAsync(s => s.DocId == id))
            throw new DocumentConflictException("Công văn đã có bằng chứng ký, không thể xóa hoặc khôi phục.");
        var expectedVersion = document.ConcurrencyVersion;
        document.IsDeleted = deleted;
        document.DeletedAt = deleted ? DateTime.UtcNow : null;
        document.DeletedById = deleted ? actor.UserId : null;
        document.DeleteReason = deleted ? reason : null;
        document.ConcurrencyVersion = Guid.NewGuid();
        _context.Entry(document).State = EntityState.Modified;
        _context.Entry(document).Property(d => d.ConcurrencyVersion).OriginalValue = expectedVersion;
        var process = new DocumentProcess
        {
            Id = Guid.NewGuid(), DocId = id, FromUserId = actor.UserId,
            Action = deleted ? DocumentAction.SoftDelete : DocumentAction.Restore,
            Comment = reason ?? "Người tạo xóa bản nháp.", Timestamp = DateTime.UtcNow
        };
        _context.DocumentProcesses.Add(process);
        if (_context.Database.IsNpgsql())
        {
            var trace = Activity.Current?.TraceId.ToString() ?? "process:" + process.Id;
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('hau.trace_id', {trace}, true)");
        }
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task CancelAsync(Guid id, DocumentActor actor, string reason, Guid expectedVersion)
    {
        if (!actor.IsAdmin || actor.UserId == Guid.Empty) throw new UnauthorizedDocumentAccessException();
        reason = reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new DocumentServiceException("Nhập lý do hủy, tối đa 1000 ký tự.");
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockAsync(id);
        var document = await GetIncludingDeletedAsync(id) ?? throw new DocumentNotFoundException(id);
        if (document.IsDeleted && document.Processes.Any(p => p.Action == DocumentAction.Cancel)) return;
        if (document.IsDeleted || document.ConcurrencyVersion != expectedVersion)
            throw new DocumentConflictException("Công văn đã thay đổi hoặc đã xóa. Hãy tải lại danh sách trước khi hủy.");
        document.IsDeleted = true;
        document.DeletedAt = DateTime.UtcNow;
        document.DeletedById = actor.UserId;
        document.DeleteReason = reason;
        document.ConcurrencyVersion = Guid.NewGuid();
        _context.Entry(document).State = EntityState.Modified;
        _context.Entry(document).Property(d => d.ConcurrencyVersion).OriginalValue = expectedVersion;
        var process = new DocumentProcess { Id = Guid.NewGuid(), DocId = id, FromUserId = actor.UserId,
            Action = DocumentAction.Cancel, Comment = reason, Timestamp = document.DeletedAt.Value };
        _context.DocumentProcesses.Add(process);
        if (_context.Database.IsNpgsql())
        {
            var trace = Activity.Current?.TraceId.ToString() ?? "process:" + process.Id;
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('hau.trace_id', {trace}, true)");
        }
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task ReceiveAsync(Guid id, Guid actorId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockAsync(id);
        var document = await _context.Documents.Include(d => d.DocType).SingleOrDefaultAsync(d => d.Id == id)
            ?? throw new DocumentNotFoundException(id);
        if (document.DocType?.TypeName != "CongVanDen" || document.Status != DocumentStatus.Draft)
            throw new DocumentConflictException("Chỉ tiếp nhận chính thức công văn đến đang ở trạng thái Nháp.");
        if (document.OfficiallyReceivedAt.HasValue) return;
        document.OfficiallyReceivedAt = DateTime.UtcNow;
        document.ConcurrencyVersion = Guid.NewGuid();
        _context.DocumentProcesses.Add(new DocumentProcess
        {
            Id = Guid.NewGuid(), DocId = id, FromUserId = actorId, Action = DocumentAction.Receive,
            Timestamp = DateTime.UtcNow, Comment = "Tiếp nhận chính thức công văn đến."
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SubmitIncomingAsync(Guid id, Guid actorId, Guid expectedVersion, string? comment)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockAsync(id);
        var document = await _context.Documents.Include(d => d.DocType).SingleOrDefaultAsync(d => d.Id == id)
            ?? throw new DocumentNotFoundException(id);
        if (document.DocType?.TypeName != "CongVanDen" || document.Status != DocumentStatus.Draft)
            throw new DocumentConflictException("Chỉ trình chỉ đạo công văn đến đang ở trạng thái Nháp.");
        if (expectedVersion == Guid.Empty || expectedVersion != document.ConcurrencyVersion)
            throw new DocumentConflictException("Công văn đã thay đổi. Hãy tải lại trước khi trình chỉ đạo.");
        if (await _context.SignatureEvidence.AnyAsync(s => s.DocId == id))
            throw new DocumentConflictException("Hồ sơ đang Nháp nhưng đã có bằng chứng ký theo quy trình cũ. Cần đối soát trước khi trình lại.");
        if (!document.MinioPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new DocumentServiceException("Cần tải bản PDF để lãnh đạo ký chỉ đạo và ký phân công. Ảnh vẫn có thể dùng để OCR.");
        if (comment?.Length > 1000) throw new DocumentServiceException("Ghi chú tối đa 1000 ký tự.");
        var now = DateTime.UtcNow;
        if (!document.OfficiallyReceivedAt.HasValue)
        {
            document.OfficiallyReceivedAt = now;
            _context.DocumentProcesses.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = id, FromUserId = actorId,
                Action = DocumentAction.Receive, Timestamp = now, Comment = "Tiếp nhận công văn đến trước khi trình Ban Giám hiệu." });
        }
        document.Status = DocumentStatus.PendingDirection;
        document.FirstSubmittedAt ??= now.AddTicks(10);
        document.ConcurrencyVersion = Guid.NewGuid();
        _context.DocumentProcesses.Add(new DocumentProcess { Id = Guid.NewGuid(), DocId = id, FromUserId = actorId,
            Action = "SubmitIncoming", Timestamp = now.AddTicks(10), Comment = string.IsNullOrWhiteSpace(comment) ? "Trình Ban Giám hiệu ký chỉ đạo công văn đến." : comment.Trim() });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Documents.AnyAsync(d => d.Id == id);
    }

    public async Task<DocumentStatisticsDto> GetStatisticsAsync(Guid userId, DateTime todayStartUtc, DateTime tomorrowStartUtc)
    {
        // Creation is the earliest Submit process, not the most recent submission or edit.
        var documents = _context.Documents.AsNoTracking().Select(d => new
        {
            d.Status, d.MinioPath, d.OcrDataRaw,
            CreatedAt = d.CreatedAt ?? d.Processes.Where(p => p.Action == DocumentAction.Submit || p.Action == DocumentAction.Create)
                .Select(p => (DateTime?)p.Timestamp).Min(),
            CreatedBy = d.CreatedById ?? d.Processes.Where(p => p.Action == DocumentAction.Submit || p.Action == DocumentAction.Create)
                .OrderBy(p => p.Timestamp).ThenBy(p => p.Id).Select(p => (Guid?)p.FromUserId).FirstOrDefault(),
            AssignedByMe = d.Processes.Any(p => p.Action == DocumentAction.Assign && p.FromUserId == userId)
        });
        return await documents.GroupBy(d => 1).Select(group => new DocumentStatisticsDto
        {
            TotalDocuments = group.Count(),
            TodayDocuments = group.Count(d => d.CreatedAt >= todayStartUtc && d.CreatedAt < tomorrowStartUtc),
            PendingDocuments = group.Count(d => d.Status == DocumentStatus.PendingDeptReview || d.Status == DocumentStatus.DeptSigned
                || d.Status == DocumentStatus.PendingDirectorSign || d.Status == DocumentStatus.DirectorSigned
                || d.Status == DocumentStatus.PendingDirection || d.Status == DocumentStatus.PendingAssignment || d.Status == DocumentStatus.IncomingAssigned),
            PublishedDocuments = group.Count(d => d.Status == DocumentStatus.Published),
            PendingOcrDocuments = group.Count(d => d.MinioPath.Trim() != "" && !d.MinioPath.ToLower().EndsWith(".docx") && d.OcrDataRaw == null),
            MyDraftDocuments = group.Count(d => d.Status == DocumentStatus.Draft && d.CreatedBy == userId),
            MyPendingDocuments = group.Count(d => d.CreatedBy == userId && (d.Status == DocumentStatus.PendingDeptReview
                || d.Status == DocumentStatus.DeptSigned || d.Status == DocumentStatus.PendingDirectorSign
                || d.Status == DocumentStatus.PendingDirection || d.Status == DocumentStatus.PendingAssignment || d.Status == DocumentStatus.IncomingAssigned)),
            PendingDeptDocuments = group.Count(d => d.Status == DocumentStatus.PendingDeptReview),
            PendingDirectorDocuments = group.Count(d => d.Status == DocumentStatus.PendingDirectorSign),
            DirectorSignedDocuments = group.Count(d => d.Status == DocumentStatus.DirectorSigned || d.Status == DocumentStatus.Published),
            AssignedDocuments = group.Count(d => d.AssignedByMe)
        }).SingleOrDefaultAsync() ?? new DocumentStatisticsDto();
    }
}
