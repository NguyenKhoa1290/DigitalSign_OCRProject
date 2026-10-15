using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;

namespace DocumentService.Core.Interfaces;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, bool includeProcesses = false);
    Task<Document?> GetByDocNumberAsync(string docNumber);
    Task<(IEnumerable<Document> Documents, int Total)> GetAllAsync(DocumentQueryParams query);
    Task<Document> CreateAsync(Document document);
    Task<Document> UpdateAsync(Document document);
    Task<bool> ReplaceFileAsync(Guid id, string expectedPath, string newPath, string fileName);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
    Task<DocumentStatisticsDto> GetStatisticsAsync(Guid userId, DateTime todayStartUtc, DateTime tomorrowStartUtc);
}
