using Microsoft.JSInterop;

namespace HauDocumentApp.Services;

public sealed class BrowserUploadService(IJSRuntime js, UploadProgressService progress)
{
    public async Task<HttpResponseMessage> SendAsync(string url, string inputId, string name, string fieldName, long maxBytes,
        Func<Task<string?>> getToken)
    {
        var item = progress.Start(name);
        string? selection = null;
        try
        {
            selection = await js.InvokeAsync<string>("documentFiles.captureUpload", inputId, maxBytes);
            var token = await getToken();
            if (string.IsNullOrEmpty(token)) throw new HttpRequestException("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.");
            using var callback = DotNetObjectReference.Create(new UploadCallback(progress, item.Id));
            var result = await js.InvokeAsync<UploadResponse>("documentFiles.upload", CancellationToken.None, selection, url, token, fieldName, callback);
            if (result.Status == 0)
                throw new HttpRequestException(result.Error switch
                {
                    "UploadStalled" => "Tải lên không có tiến triển trong 45 giây. Hãy kiểm tra mạng rồi thử lại.",
                    "ProcessingTimeout" => "Tệp đã gửi nhưng máy chủ chưa trả kết quả sau 3 phút. Kiểm tra trước khi gửi lại.",
                    "UploadAborted" => "Đã ngừng tải tệp.",
                    _ => "Mất kết nối khi tải tệp lên. Hãy kiểm tra mạng rồi thử lại."
                });
            var response = new HttpResponseMessage((System.Net.HttpStatusCode)result.Status) { Content = new ByteArrayContent(result.Body) };
            foreach (var header in result.Headers)
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value)) response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            progress.Finish(item.Id, response.IsSuccessStatusCode, (int)response.StatusCode >= 500 ? "Tệp đã tải lên nhưng máy chủ xử lý thất bại." : null);
            return response;
        }
        catch (HttpRequestException ex) { progress.Finish(item.Id, false, ex.Message); throw; }
        catch { progress.Finish(item.Id, false); throw; }
        finally { if (selection != null) await js.InvokeVoidAsync("documentFiles.releaseUpload", selection); }
    }
    public sealed class UploadCallback(UploadProgressService progress, Guid id)
    {
        [JSInvokable] public void Report(long loaded, long total, bool computable) => progress.Report(id, loaded, total, computable);
        [JSInvokable] public void Uploaded() => progress.Uploaded(id);
    }
    public sealed class UploadResponse
    {
        public int Status { get; set; }
        public string? Error { get; set; }
        public byte[] Body { get; set; } = [];
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
