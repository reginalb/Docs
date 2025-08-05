using System.Text.Json.Serialization;

namespace ApiTimeoutHandler.Models;

public class KafkaEventModel
{
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("apiId")]
    public string ApiId { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("scheduledTime")]
    public DateTime ScheduledTime { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class EventTypes
{
    public const string SCHEDULED_API_CALL = "scheduled_api_call";
    public const string RETRY_API_CALL = "retry_api_call";
    public const string CACHE_REFRESH = "cache_refresh";
}