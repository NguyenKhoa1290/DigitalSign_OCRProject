using System.Net.Http.Json;
using System.Text.Json;
using DocumentService.Core.Exceptions;
using Npgsql;

namespace DocumentService.API.Services;

public sealed class OcrJobWorker(OcrJobStore jobs,IOcrJobQueue queue,IHttpClientFactory clients,ILogger<OcrJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection=await jobs.OpenAsync(stoppingToken);
                await using var acquire=new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)",connection);
                acquire.Parameters.AddWithValue("key",OcrJobStore.WorkerLock);
                if((bool)(await acquire.ExecuteScalarAsync(stoppingToken))!)
                {
                    try
                    {
                        await jobs.RecoverInterruptedAsync(connection,stoppingToken);
                        using var consumer=await queue.SubscribeAsync(stoppingToken);
                        while(!stoppingToken.IsCancellationRequested)
                        {
                            var delivery=await consumer.ReceiveAsync(stoppingToken);
                            if(delivery==null)continue;
                            if(delivery.JobId is not Guid jobId)
                            {
                                logger.LogWarning("Invalid OCR Kafka command skipped (no AI call).");
                                consumer.Commit(delivery);continue;
                            }
                            var job=await jobs.ClaimAsync(connection,jobId,stoppingToken);
                            if(job==null) {consumer.Commit(delivery);continue;}
                            try
                            {
                                if(!await OcrJobStore.IsOperatorForDocumentAsync(connection,null,job.ActorId,job.Info.DocumentId,stoppingToken))
                                    throw new DocumentServiceException("Tài khoản không còn quyền chạy OCR.");
                                var result=await RecognizeAsync(job,stoppingToken);
                                await jobs.CompleteAsync(connection,job,result,stoppingToken);
                            }
                            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {throw;}
                            catch(Exception ex)
                            {
                                var error=ex is DocumentServiceException ? ex.Message : ex is OperationCanceledException
                                    ? "OCR quá thời gian xử lý. Hệ thống không tự chạy lại; bạn có thể chọn model khác."
                                    : "Không xử lý được OCR. Hãy thử lại hoặc chọn model Gemini khác.";
                                logger.LogWarning("OCR job {JobId} failed: {ErrorType}",job.Info.Id,ex.GetType().Name);
                                await jobs.FailAsync(connection,job,error,stoppingToken);
                            }
                            // Persist the result/failure and notification before acknowledging Kafka.
                            consumer.Commit(delivery);
                        }
                    }
                    finally
                    {
                        await using var release=new NpgsqlCommand("SELECT pg_advisory_unlock(@key)",connection);
                        release.Parameters.AddWithValue("key",OcrJobStore.WorkerLock);
                        await release.ExecuteScalarAsync(CancellationToken.None);
                    }
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogWarning("OCR queue worker deferred: {ErrorType}",ex.GetType().Name);}
            try{await Task.Delay(1500,stoppingToken);}catch(OperationCanceledException){return;}
        }
    }

    public async Task<string> RecognizeAsync(OcrJobRow job,CancellationToken token)
    {
        using var client=clients.CreateClient("ocr-background");
        using var response=await client.PostAsJsonAsync("api/ocr/internal/gemini-job",new
        {docId=job.Info.DocumentId,sourcePath=job.SourcePath,modelId=job.Info.ModelId,pages=job.Info.Pages},token);
        var text=await response.Content.ReadAsStringAsync(token);
        if(!response.IsSuccessStatusCode)
        {
            var error="Dịch vụ OCR không xử lý được tài liệu. Hãy thử model khác hoặc thử lại sau.";
            try
            {
                using var json=JsonDocument.Parse(text);
                if(json.RootElement.TryGetProperty("detail",out var detail)&&detail.ValueKind==JsonValueKind.String)
                    error=detail.GetString()??error;
            }
            catch(JsonException){}
            throw new DocumentServiceException(error[..Math.Min(error.Length,900)]);
        }
        return text;
    }
}
