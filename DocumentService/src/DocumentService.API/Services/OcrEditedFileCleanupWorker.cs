namespace DocumentService.API.Services;

public sealed class OcrEditedFileCleanupWorker(IServiceScopeFactory scopes,ILogger<OcrEditedFileCleanupWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();
                if(await scope.ServiceProvider.GetRequiredService<OcrEditedFileStore>().CleanupOneAsync(stoppingToken))continue;
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogWarning("OCR edited file cleanup deferred: {ErrorType}",ex.GetType().Name);}
            try{await Task.Delay(30000,stoppingToken);}catch(OperationCanceledException){return;}
        }
    }
}
