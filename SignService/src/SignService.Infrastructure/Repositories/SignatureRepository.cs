using Microsoft.EntityFrameworkCore;
using SignService.Core.Entities;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;

namespace SignService.Infrastructure.Repositories;

public class SignatureRepository : ISignatureRepository
{
    private readonly AppDbContext _db;

    public SignatureRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Signature?> GetByIdAsync(Guid id)
    {
        return await _db.Signatures.FindAsync(id);
    }

    public async Task<IEnumerable<Signature>> GetByDocumentIdAsync(Guid docId)
    {
        return await _db.Signatures
            .Where(s => s.DocId == docId)
            .OrderBy(s => s.SignedAt)
            .ToListAsync();
    }

    public async Task<Signature> CreateAsync(Signature signature)
    {
        signature.Id = Guid.NewGuid();
        _db.Signatures.Add(signature);
        await _db.SaveChangesAsync();
        return signature;
    }

    public async Task<bool> HasSignatureAsync(Guid docId, string signatureType)
    {
        return await _db.Signatures
            .AnyAsync(s => s.DocId == docId && s.SignatureType == signatureType);
    }
}
