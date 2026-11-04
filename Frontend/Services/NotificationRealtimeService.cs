using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HauDocumentApp.Services;

public sealed class NotificationRealtimeService(HttpClient http, AuthService auth)
{
    private CancellationTokenSource? _stop;
    private Task? _connection;
    private CancellationTokenSource? _streamStop;
    private Guid? _watchedDocument;
    public long UnreadCount { get; private set; }
    public bool Connected { get; private set; }
    public event Action? Changed;
    public event Action? Received;
    public event Action<Guid, Guid?>? DocumentChanged;
    public void WatchDocument(Guid? document)
    {
        if (_watchedDocument == document) return;
        _watchedDocument = document;
        _streamStop?.Cancel(); // Reopen the same authenticated SSE connection for the current document.
    }
    public void Start()
    {
        if (_stop != null) return;
        _stop = new(); _connection = Connect(_stop.Token);
    }
    private async Task Connect(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var watchedDocument = _watchedDocument;
            using var streamStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _streamStop = streamStop;
            if (watchedDocument != _watchedDocument) streamStop.Cancel();
            var requestToken = streamStop.Token;
            try
            {
                var access = await auth.GetValidAccessTokenAsync();
                if (string.IsNullOrEmpty(access)) { UnreadCount = 0; Changed?.Invoke(); return; }
                var path = "api/notifications/stream" + (watchedDocument.HasValue ? $"?documentId={watchedDocument}" : "");
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.SetBrowserResponseStreamingEnabled(true);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
                if (response.StatusCode == HttpStatusCode.NotFound && watchedDocument.HasValue)
                {
                    DocumentChanged?.Invoke(watchedDocument.Value, null);
                    if (_watchedDocument == watchedDocument) WatchDocument(null);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized) await auth.RefreshAccessTokenAsync(access);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(requestToken);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(requestToken) is { } line)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    using var data = JsonDocument.Parse(line[6..]);
                    UnreadCount = Math.Max(0, data.RootElement.GetProperty("unreadCount").GetInt64());
                    if (watchedDocument.HasValue && _watchedDocument == watchedDocument &&
                        data.RootElement.TryGetProperty("documentId", out var document) && document.ValueKind == JsonValueKind.String &&
                        document.TryGetGuid(out var id) && id == watchedDocument &&
                        data.RootElement.TryGetProperty("documentVersion", out var version))
                    {
                        if (version.ValueKind == JsonValueKind.Null) DocumentChanged?.Invoke(id, null);
                        else if (version.ValueKind == JsonValueKind.String && version.TryGetGuid(out var parsed)) DocumentChanged?.Invoke(id, parsed);
                    }
                    Connected = true; Changed?.Invoke(); Received?.Invoke();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch { /* Retain the last count until a successful authoritative snapshot. */ }
            finally { if (_streamStop == streamStop) _streamStop = null; }
            Connected = false; Changed?.Invoke();
            if (watchedDocument != _watchedDocument) continue;
            try { await Task.Delay(2000, token); } catch (OperationCanceledException) { break; }
        }
    }
    public async Task StopAsync()
    {
        if (_stop == null) return;
        _stop.Cancel();
        if (_connection != null) await _connection;
        _stop.Dispose(); _stop = null; _connection = null;
        Connected = false; UnreadCount = 0;
    }
}
