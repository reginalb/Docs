using ApiTimeoutHandler.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ApiTimeoutHandler.Services;

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IOptions<KafkaSettings> settings, ILogger<KafkaProducer> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            ClientId = _settings.ClientId,
            Acks = Acks.All,
            RetryBackoffMs = 1000,
            MessageSendMaxRetries = 3,
            RequestTimeoutMs = 30000,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Error}", e.Reason))
            .Build();
    }

    public async Task<bool> PublishEventAsync(KafkaEventModel eventModel)
    {
        try
        {
            var json = JsonSerializer.Serialize(eventModel);
            var message = new Message<string, string>
            {
                Key = eventModel.ApiId,
                Value = json,
                Headers = new Headers
                {
                    { "event-type", System.Text.Encoding.UTF8.GetBytes(eventModel.EventType) },
                    { "event-id", System.Text.Encoding.UTF8.GetBytes(eventModel.EventId) },
                    { "created-at", System.Text.Encoding.UTF8.GetBytes(eventModel.CreatedAt.ToString("O")) }
                }
            };

            var result = await _producer.ProduceAsync(_settings.TopicName, message);
            
            _logger.LogInformation("Published event {EventId} for API ID {ApiId} to partition {Partition} at offset {Offset}",
                eventModel.EventId, eventModel.ApiId, result.Partition.Value, result.Offset.Value);
            
            return true;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventId} for API ID {ApiId}: {Error}",
                eventModel.EventId, eventModel.ApiId, ex.Error.Reason);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing event {EventId} for API ID {ApiId}",
                eventModel.EventId, eventModel.ApiId);
            return false;
        }
    }

    public async Task<bool> PublishScheduledApiCallAsync(string apiId, DateTime scheduledTime, int priority = 1)
    {
        var eventModel = new KafkaEventModel
        {
            ApiId = apiId,
            EventType = EventTypes.SCHEDULED_API_CALL,
            ScheduledTime = scheduledTime,
            Priority = priority
        };

        return await PublishEventAsync(eventModel);
    }

    public async Task<bool> PublishRetryEventAsync(string apiId, int retryCount)
    {
        var eventModel = new KafkaEventModel
        {
            ApiId = apiId,
            EventType = EventTypes.RETRY_API_CALL,
            ScheduledTime = DateTime.UtcNow.AddMinutes(Math.Pow(2, retryCount)), // Exponential backoff
            RetryCount = retryCount,
            Priority = 2 // Higher priority for retries
        };

        return await PublishEventAsync(eventModel);
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
    }
}

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string TopicName { get; set; } = "api-timeout-events";
    public string ClientId { get; set; } = "api-timeout-handler";
    public string GroupId { get; set; } = "api-timeout-handler-group";
}