namespace SignService.Core.Interfaces;

public interface ISigningDocumentPreparer
{
    Task<byte[]> PrepareAsync(Guid documentId);
}
