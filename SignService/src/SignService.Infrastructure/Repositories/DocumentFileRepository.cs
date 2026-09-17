using Microsoft.EntityFrameworkCore;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

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
}

