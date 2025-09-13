using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ApiTimeoutHandler.Models;

public class ApiDataModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("apiId")]
    public string ApiId { get; set; } = string.Empty;

    [BsonElement("data")]
    public BsonDocument Data { get; set; } = new();

    [BsonElement("lastUpdated")]
    public DateTime LastUpdated { get; set; }

    [BsonElement("isFromCache")]
    public bool IsFromCache { get; set; }

    [BsonElement("apiCallSuccess")]
    public bool ApiCallSuccess { get; set; }

    [BsonElement("errorMessage")]
    public string? ErrorMessage { get; set; }

    [BsonElement("responseTime")]
    public long ResponseTimeMs { get; set; }

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}