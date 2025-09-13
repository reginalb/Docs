using ApiTimeoutHandler.Models;

namespace ApiTimeoutHandler.Services;

public interface IKafkaProducer
{
    Task<bool> PublishEventAsync(KafkaEventModel eventModel);
    Task<bool> PublishScheduledApiCallAsync(string apiId, DateTime scheduledTime, int priority = 1);
    Task<bool> PublishRetryEventAsync(string apiId, int retryCount);
}