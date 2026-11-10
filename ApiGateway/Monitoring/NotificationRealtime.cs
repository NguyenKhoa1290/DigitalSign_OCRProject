using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ApiGateway.Monitoring;

// Only invalidations travel over NOTIFY; message contents remain protected by the user query.
public sealed class NotificationSignals
{
    private readonly ConcurrentDictionary<Guid, (Guid User, Guid? Document, Channel<bool> Channel)> _clients = new();
    public (Guid Id, ChannelReader<bool> Reader) Subscribe(Guid user, Guid? document = null)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        _clients[id] = (user, document, channel);
        return (id, channel.Reader);
    }
    public void Unsubscribe(Guid id) { if (_clients.TryRemove(id, out var client)) client.Channel.Writer.TryComplete(); }
    public void Publish(Guid? user = null)
    {
        foreach (var client in _clients.Values)
            if (user == null || client.User == user) client.Channel.Writer.TryWrite(true);
    }
    public void PublishDocument(Guid document)
    {
        foreach (var client in _clients.Values)
            if (client.Document == document) client.Channel.Writer.TryWrite(true);
    }
}

public sealed class NotificationListener(NpgsqlDataSource source, NotificationSignals signals, ILogger<NotificationListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await source.OpenConnectionAsync(stoppingToken);
                connection.Notification += (_, e) =>
                {
                    if (!Guid.TryParse(e.Payload, out var id)) return;
                    if (e.Channel == "hau_document_updates") signals.PublishDocument(id);
                    else if (e.Channel == "hau_notifications") signals.Publish(id);
                };
                await using var listen = new NpgsqlCommand("LISTEN hau_notifications; LISTEN hau_document_updates", connection);
                await listen.ExecuteNonQueryAsync(stoppingToken);
                signals.Publish(); // Reconcile changes missed during a disconnected listener.
                while (!stoppingToken.IsCancellationRequested) await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Notification listener reconnecting ({ErrorType})", ex.GetType().Name);
                try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}

[ApiController, Route("api/notifications/stream"), Authorize]
public sealed class NotificationStreamController(EventStore store, NotificationSignals signals) : ControllerBase
{
    [HttpGet]
    public async Task Stream([FromQuery] Guid? documentId = null)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var user))
        { Response.StatusCode = 401; return; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2)); // Reconnect with a refreshed and revalidated JWT.
        var token = lifetime.Token;
        // Recheck scope on subscription and each update; never stream OCR text.
        if (documentId.HasValue && await store.GetDocumentVersionAsync(documentId.Value, user, token) == null)
        { Response.StatusCode = 404; return; }
        var subscription = signals.Subscribe(user, documentId);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await SendCount();
            while (!token.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(token);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    await subscription.Reader.ReadAsync(heartbeat.Token);
                    while (subscription.Reader.TryRead(out _)) { }
                    await SendCount();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { await Response.WriteAsync(": keep-alive\n\n", token); await Response.Body.FlushAsync(token); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { signals.Unsubscribe(subscription.Id); }

        async Task SendCount()
        {
            var count = await store.GetUnreadCountAsync(user, token);
            var json = documentId.HasValue ? JsonSerializer.Serialize(new
            {
                unreadCount = count, documentId,
                documentVersion = await store.GetDocumentVersionAsync(documentId.Value, user, token)
            }) : $"{{\"unreadCount\":{count}}}";
            await Response.WriteAsync($"data: {json}\n\n", token);
            await Response.Body.FlushAsync(token);
        }
    }
}
