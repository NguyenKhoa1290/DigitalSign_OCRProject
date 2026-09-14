using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Infrastructure.Repositories;

public class DocumentTypeRepository : IDocumentTypeRepository
{
    private readonly AppDbContext _context;

    public DocumentTypeRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DocumentType>> GetAllAsync()
    {
        return await _context.DocumentTypes
            .AsNoTracking()
            .OrderBy(t => t.TypeName)
            .ToListAsync();
    }

    public async Task<DocumentType?> GetByIdAsync(Guid id)
    {
        return await _context.DocumentTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<DocumentType?> GetByNameAsync(string typeName)
    {
        return await _context.DocumentTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TypeName == typeName);
    }
}
