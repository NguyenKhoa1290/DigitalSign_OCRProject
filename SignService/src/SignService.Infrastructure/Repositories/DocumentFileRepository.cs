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
            .Select(d => d.MinioPath)
            .FirstOrDefaultAsync();
    }

    public async Task<IDocumentSigningLease> BeginSigningAsync(Guid docId)
    {
        // Persist an irreversible signing marker BEFORE touching the external PDF.
        // A failed signing attempt may already have changed the object; it cannot become deletable.
        await using (var start = await _context.Database.BeginTransactionAsync())
        {
            await LockAsync(docId);
            var document = await _context.DocumentFiles.AsNoTracking().SingleOrDefaultAsync(d => d.Id == docId)
                ?? throw new DocumentNotFoundException(docId);
            if (string.IsNullOrWhiteSpace(document.MinioPath)) throw new DocumentNotFoundException(docId);
            var version = Guid.NewGuid();
            await _context.DocumentFiles.Where(d => d.Id == docId).ExecuteUpdateAsync(update => update
                .SetProperty(d => d.SigningStartedAt, d => d.SigningStartedAt ?? DateTime.UtcNow)
                .SetProperty(d => d.ConcurrencyVersion, version));
            await start.CommitAsync();
        }
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

    private Task LockAsync(Guid id) => _context.Database.IsNpgsql()
        ? _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\"={id} FOR UPDATE")
        : Task.CompletedTask;

    private sealed class SigningLease(IDbContextTransaction transaction) : IDocumentSigningLease
    {
        public Task CompleteAsync() => transaction.CommitAsync();
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

