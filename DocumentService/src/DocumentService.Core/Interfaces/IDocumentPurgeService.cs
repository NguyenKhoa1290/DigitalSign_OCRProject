using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;

namespace DocumentService.Core.Interfaces;

public interface IDocumentPurgeService
{
    Task<DocumentPurgeResult> PurgeAsync(Guid id, DocumentActor actor, DocumentPurgeDto request);
    Task CleanupPendingAsync(CancellationToken cancellationToken = default);
}
