using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
            .OrderByDescending(d => d.IssuedDate)
            .ThenByDescending(d => d.Title)
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
        _context.Documents.Update(document);
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document is null) return false;

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Documents.AnyAsync(d => d.Id == id);
    }
}
