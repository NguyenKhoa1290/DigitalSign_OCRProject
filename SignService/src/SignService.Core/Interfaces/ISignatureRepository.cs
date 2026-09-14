using SignService.Core.Entities;

namespace SignService.Core.Interfaces;

public interface ISignatureRepository
{
    Task<Signature?> GetByIdAsync(Guid id);
    Task<IEnumerable<Signature>> GetByDocumentIdAsync(Guid docId);
    Task<Signature> CreateAsync(Signature signature);
    Task<bool> HasSignatureAsync(Guid docId, string signatureType);
}
