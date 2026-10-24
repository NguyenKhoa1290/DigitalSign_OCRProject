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
    Task<Document?> GetIncludingDeletedAsync(Guid id);
    Task<(IEnumerable<Document> Documents, int Total)> GetDeletedAsync(DocumentQueryParams query);
    Task SetDeletedAsync(Guid id, DocumentActor actor, string? reason, bool deleted);
    Task CancelAsync(Guid id, DocumentActor actor, string reason, Guid expectedVersion);
    Task ReceiveAsync(Guid id, Guid actorId);
    Task SubmitIncomingAsync(Guid id, Guid actorId, Guid expectedVersion, string? comment);
    Task<bool> ExistsAsync(Guid id);
    Task<DocumentStatisticsDto> GetStatisticsAsync(Guid userId, DateTime todayStartUtc, DateTime tomorrowStartUtc);
}
