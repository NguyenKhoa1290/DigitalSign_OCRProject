using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace DocumentService.API.Services;

public record OcrJobDelivery(Guid? JobId, object Receipt);

public interface IOcrJobSubscription : IDisposable
{
    Task<OcrJobDelivery?> ReceiveAsync(CancellationToken token);
    void Commit(OcrJobDelivery delivery);
}

public interface IOcrJobQueue
{
    Task PublishAsync(Guid jobId, CancellationToken token);
    Task<IOcrJobSubscription> SubscribeAsync(CancellationToken token);
}

public sealed class OcrJobKafkaQueue : IOcrJobQueue, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _servers, _topic, _group;

    public OcrJobKafkaQueue(IConfiguration configuration)
    {
        _servers = configuration["KafkaSettings:BootstrapServers"] ?? "localhost:9092";
        _topic = configuration["KafkaSettings:TopicOcrRequested"] ?? "ocr.gemini.requested.v1";
        _group = configuration["KafkaSettings:OcrConsumerGroup"] ?? "document-gemini-ocr-v1";
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _servers, EnableIdempotence = true, Acks = Acks.All,
            MessageTimeoutMs = 5000, RetryBackoffMs = 500
        }).Build();
    }

    public async Task PublishAsync(Guid jobId, CancellationToken token)
    {
        // No attachment bytes, user token or API key enters Kafka.
        var report = await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = jobId.ToString(), Value = JsonSerializer.Serialize(new {schemaVersion = 1, jobId})
        }, token);
        if (report.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException("Kafka has not confirmed durable delivery.");
    }

    public async Task<IOcrJobSubscription> SubscribeAsync(CancellationToken token)
    {
        // Consumer auto-creation is not reliable on every broker configuration.
        // Explicit creation is idempotent and also works on a fresh Docker volume.
        using var admin = new AdminClientBuilder(new AdminClientConfig {BootstrapServers = _servers}).Build();
        try
        {
            await admin.CreateTopicsAsync(new[] {new TopicSpecification {Name = _topic, NumPartitions = 1, ReplicationFactor = 1}},
                new CreateTopicsOptions {RequestTimeout = TimeSpan.FromSeconds(5), OperationTimeout = TimeSpan.FromSeconds(5)}).WaitAsync(token);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists)) { }
        return new Subscription(_servers, _topic, _group);
    }
    public void Dispose() => _producer.Dispose();

    private sealed class Subscription : IOcrJobSubscription
    {
        private readonly IConsumer<string, string> _consumer;
        public Subscription(string servers, string topic, string group)
        {
            _consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = servers, GroupId = group, AutoOffsetReset = AutoOffsetReset.Earliest,
                AllowAutoCreateTopics = true,
                EnableAutoCommit = false, EnableAutoOffsetStore = false,
                // The HTTP OCR request is capped at 30 minutes; leave time to persist and commit.
                MaxPollIntervalMs = 35 * 60 * 1000, SessionTimeoutMs = 45000
            }).Build();
            _consumer.Subscribe(topic);
        }

        public Task<OcrJobDelivery?> ReceiveAsync(CancellationToken token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var record = _consumer.Consume(TimeSpan.FromSeconds(1));
            if (record == null) return null;
            Guid? jobId = null;
            try
            {
                using var payload = JsonDocument.Parse(record.Message.Value);
                if (payload.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
                    payload.RootElement.GetProperty("jobId").TryGetGuid(out var parsed) && parsed != Guid.Empty)
                    jobId = parsed;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException) { }
            return new OcrJobDelivery(jobId, record);
        }, token);

        public void Commit(OcrJobDelivery delivery) => _consumer.Commit((ConsumeResult<string, string>)delivery.Receipt);
        public void Dispose()
        {
            try { _consumer.Close(); }
            finally { _consumer.Dispose(); }
        }
    }
}
