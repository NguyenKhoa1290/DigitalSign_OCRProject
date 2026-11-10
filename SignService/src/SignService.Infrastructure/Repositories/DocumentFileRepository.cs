using Microsoft.EntityFrameworkCore;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;
using SignService.Core.Exceptions;
using Microsoft.EntityFrameworkCore.Storage;

namespace SignService.Infrastructure.Repositories;

public class DocumentFileRepository : IDocumentFileRepository
{
    private readonly AppDbContext _context;

    public DocumentFileRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetMinioPathAsync(Guid docId)
    {
        return await _context.DocumentFiles
            .AsNoTracking()
            .Where(d => d.Id == docId)
            .Select(d => d.SignedPdfPath ?? d.MinioPath)
            .FirstOrDefaultAsync();
    }

    public Task<bool> IsIncomingAsync(Guid docId)
        => _context.DocumentFiles.AnyAsync(d => d.Id == docId && _context.DocumentTypes.Any(t => t.Id == d.DocTypeId && t.TypeName == "CongVanDen"));

    public async Task<IDocumentSigningLease> BeginSigningAsync(Guid docId)
    {
        // Signing writes a new immutable object. Failed preparation must not lock
        // an unsigned draft; serialize source/OCR edits through this row lock.
        var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await LockAsync(docId);
            if (!await _context.DocumentFiles.AnyAsync(d => d.Id == docId))
                throw new DocumentNotFoundException(docId);
            return new SigningLease(transaction);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public async Task SaveSignedPdfAsync(Guid docId, string objectName)
    {
        var document = await _context.DocumentFiles.SingleAsync(d => d.Id == docId);
        document.SignedPdfPath = objectName;
        document.SigningStartedAt ??= DateTime.UtcNow;
        document.ConcurrencyVersion = Guid.NewGuid();
        await _context.SaveChangesAsync();
    }

    public async Task ValidateSigningStageAsync(Guid docId, string signatureType)
    {
        var document=await _context.DocumentFiles.AsNoTracking().SingleAsync(d=>d.Id==docId);
        var legacy=document.Status=="DeptSigned" && document.SignedPdfPath==null && !await _context.Signatures.AnyAsync(s=>s.DocId==docId);
        if (signatureType==Core.Entities.SignatureType.PersonalSignature && document.Status!="PendingDeptReview" && !legacy
            || signatureType==Core.Entities.SignatureType.LegalSeal && document.Status!="PendingDirectorSign")
            throw new IncomingConflictException("Giai đoạn công văn đã thay đổi. Hãy tải lại trước khi ký.");
    }

    private Task LockAsync(Guid id) => _context.Database.IsNpgsql()
        ? _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\"={id} FOR UPDATE")
        : Task.CompletedTask;

    private sealed class SigningLease(IDbContextTransaction transaction) : IDocumentSigningLease
    {
        public Task CompleteAsync() => transaction.CommitAsync();
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

