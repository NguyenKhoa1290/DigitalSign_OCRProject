using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DocumentService.Infrastructure.Repositories;

public class DocumentProcessRepository : IDocumentProcessRepository
{
    private readonly AppDbContext _context;

    public DocumentProcessRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DocumentProcess>> GetByDocumentIdAsync(Guid docId)
    {
        return await _context.DocumentProcesses
            .AsNoTracking()
            .Where(p => p.DocId == docId)
            .OrderBy(p => p.Timestamp)
            .ToListAsync();
    }

    public async Task<DocumentProcess> CreateAsync(DocumentProcess process)
    {
        process.Id = Guid.NewGuid();
        process.Timestamp = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var trace = Activity.Current?.TraceId.ToString() ?? "process:" + process.Id;
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('hau.trace_id', {trace}, true)");
        await _context.DocumentProcesses.AddAsync(process);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return process;
    }
}
