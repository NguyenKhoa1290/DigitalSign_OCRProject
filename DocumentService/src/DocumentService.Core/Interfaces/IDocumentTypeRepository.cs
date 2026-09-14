using DocumentService.Core.Entities;

namespace DocumentService.Core.Interfaces;

public interface IDocumentTypeRepository
{
    Task<IEnumerable<DocumentType>> GetAllAsync();
    Task<DocumentType?> GetByIdAsync(Guid id);
    Task<DocumentType?> GetByNameAsync(string typeName);
}
