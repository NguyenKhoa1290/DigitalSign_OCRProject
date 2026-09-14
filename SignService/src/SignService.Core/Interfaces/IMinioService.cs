namespace SignService.Core.Interfaces;

public interface IMinioService
{
    Task<byte[]> DownloadFileAsync(string objectPath);
    Task UploadFileAsync(string objectPath, byte[] content, string contentType = "application/pdf");
}
