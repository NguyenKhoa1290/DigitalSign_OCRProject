namespace DocumentService.API.Services;

public sealed class OcrJobOutbox(OcrJobStore jobs, IOcrJobQueue queue, ILogger<OcrJobOutbox> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await jobs.PublishNextAsync(queue, stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("OCR Kafka dispatch deferred; durable command retained: {ErrorType}", ex.GetType().Name);
            }
            try { await Task.Delay(3000, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
