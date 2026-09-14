using System.Text.Json;
using Confluent.Kafka;
using DocumentService.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentService.Infrastructure.Services;

/// <summary>
/// Kafka Producer — gửi event "DocumentUploaded" sau khi file PDF được lưu MinIO.
/// OCR Service (Python) sẽ lắng nghe topic này và tự động xử lý.
/// </summary>
public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly bool _enabled;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(IConfiguration config, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        _enabled = config.GetValue<bool>("KafkaSettings:Enabled", false);
        _topic   = config["KafkaSettings:TopicDocumentUploaded"] ?? "document.uploaded";

        var bootstrapServers = config["KafkaSettings:BootstrapServers"] ?? "localhost:9092";

        var producerConfig = new ProducerConfig
        {
            BootstrapServers       = bootstrapServers,
            Acks                   = Acks.Leader,          // đảm bảo leader nhận message
            MessageTimeoutMs       = 5000,
            EnableIdempotence      = false,
            RetryBackoffMs         = 500,
            MessageSendMaxRetries  = 3,
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();

        if (_enabled)
            _logger.LogInformation("Kafka Producer khởi tạo — servers={S} | topic={T}",
                bootstrapServers, _topic);
        else
            _logger.LogInformation("Kafka bị tắt (KafkaSettings:Enabled=false). " +
                "OCR sẽ được kích hoạt thủ công.");
    }

    /// <inheritdoc/>
    public async Task PublishDocumentUploadedAsync(Guid docId, string minioPath, string authToken)
    {
        if (!_enabled)
        {
            _logger.LogDebug("Kafka tắt — bỏ qua publish event DocumentUploaded cho doc={DocId}", docId);
            return;
        }

        // Payload OCR Service cần để xử lý
        var payload = JsonSerializer.Serialize(new
        {
            doc_id     = docId.ToString(),
            minio_path = minioPath,
            token      = authToken,
            timestamp  = DateTime.UtcNow.ToString("O"),
        });

        try
        {
            var message = new Message<string, string>
            {
                Key   = docId.ToString(),   // dùng docId làm key để đảm bảo ordering
                Value = payload,
            };

            var result = await _producer.ProduceAsync(_topic, message);

            _logger.LogInformation(
                "✅ Kafka publish OK — topic={Topic} | partition={P} | offset={O} | doc={DocId} | path={Path}",
                _topic, result.Partition.Value, result.Offset.Value, docId, minioPath);
        }
        catch (ProduceException<string, string> ex)
        {
            // Không throw — Kafka lỗi không được làm hỏng luồng upload chính
            _logger.LogError(ex,
                "❌ Kafka publish FAILED — doc={DocId}, reason={Reason}. " +
                "File đã lưu MinIO thành công, OCR cần kích hoạt thủ công.",
                docId, ex.Error.Reason);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
