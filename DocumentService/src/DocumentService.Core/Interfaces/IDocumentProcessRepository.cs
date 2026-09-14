using DocumentService.Core.Entities;

namespace DocumentService.Core.Interfaces;

public interface IDocumentProcessRepository
{
    Task<IEnumerable<DocumentProcess>> GetByDocumentIdAsync(Guid docId);
    Task<DocumentProcess> CreateAsync(DocumentProcess process);
}
