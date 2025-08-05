using ApiTimeoutHandler.Models;
using ApiTimeoutHandler.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System.Diagnostics;

namespace ApiTimeoutHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApiDataController : ControllerBase
{
    private readonly IExternalApiService _externalApiService;
    private readonly IMongoRepository _mongoRepository;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<ApiDataController> _logger;
    private readonly TimeSpan _cacheWindow = TimeSpan.FromHours(1);

    public ApiDataController(
        IExternalApiService externalApiService,
        IMongoRepository mongoRepository,
        IKafkaProducer kafkaProducer,
        ILogger<ApiDataController> logger)
    {
        _externalApiService = externalApiService;
        _mongoRepository = mongoRepository;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    [HttpGet("{apiId}")]
    public async Task<IActionResult> GetApiData(string apiId)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Received request for API ID: {ApiId}", apiId);

            // Check if we have fresh data in cache (within 1-hour window)
            var cachedData = await _mongoRepository.GetByApiIdAsync(apiId);
            
            if (cachedData != null && IsDataFresh(cachedData, _cacheWindow))
            {
                _logger.LogInformation("Returning cached data for API ID: {ApiId}, age: {Age} minutes", 
                    apiId, (DateTime.UtcNow - cachedData.LastUpdated).TotalMinutes);

                stopwatch.Stop();
                return Ok(new ApiResponse
                {
                    Success = true,
                    Data = cachedData.Data,
                    IsFromCache = true,
                    LastUpdated = cachedData.LastUpdated,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Message = "Data retrieved from cache"
                });
            }

            // Try to call external API
            _logger.LogInformation("Cache miss or stale data for API ID: {ApiId}, calling external API", apiId);
            
            var apiResult = await _externalApiService.CallExternalApiAsync(apiId);
            
            if (apiResult.IsSuccess)
            {
                // Success - update cache and return fresh data
                var newCacheEntry = new ApiDataModel
                {
                    ApiId = apiId,
                    Data = apiResult.Data != null ? BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(apiResult.Data)) : new BsonDocument(),
                    LastUpdated = DateTime.UtcNow,
                    IsFromCache = false,
                    ApiCallSuccess = true,
                    ResponseTimeMs = apiResult.ResponseTimeMs,
                    ExpiresAt = DateTime.UtcNow.Add(_cacheWindow)
                };

                await _mongoRepository.UpsertAsync(newCacheEntry);
                
                _logger.LogInformation("Successfully updated cache for API ID: {ApiId}", apiId);

                stopwatch.Stop();
                return Ok(new ApiResponse
                {
                    Success = true,
                    Data = newCacheEntry.Data,
                    IsFromCache = false,
                    LastUpdated = newCacheEntry.LastUpdated,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Message = "Data retrieved from external API"
                });
            }
            else
            {
                // API call failed - check if we have any cached data to fall back to
                if (cachedData != null)
                {
                    _logger.LogWarning("External API failed for ID: {ApiId}, returning stale cached data. Error: {Error}", 
                        apiId, apiResult.ErrorMessage);

                    // Schedule retry if it was a timeout
                    if (apiResult.IsTimeout)
                    {
                        await _kafkaProducer.PublishRetryEventAsync(apiId, 1);
                        _logger.LogInformation("Scheduled retry event for API ID: {ApiId} due to timeout", apiId);
                    }

                    stopwatch.Stop();
                    return Ok(new ApiResponse
                    {
                        Success = true,
                        Data = cachedData.Data,
                        IsFromCache = true,
                        LastUpdated = cachedData.LastUpdated,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Message = $"External API failed, returning cached data. Error: {apiResult.ErrorMessage}",
                        Warning = "Data may be stale due to API failure"
                    });
                }
                else
                {
                    // No cached data available and API failed
                    _logger.LogError("No cached data available and external API failed for ID: {ApiId}. Error: {Error}", 
                        apiId, apiResult.ErrorMessage);

                    // Schedule retry for timeout
                    if (apiResult.IsTimeout)
                    {
                        await _kafkaProducer.PublishRetryEventAsync(apiId, 1);
                    }

                    stopwatch.Stop();
                    return StatusCode(503, new ApiResponse
                    {
                        Success = false,
                        Data = null,
                        IsFromCache = false,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Message = "External API failed and no cached data available",
                        Error = apiResult.ErrorMessage
                    });
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error processing request for API ID: {ApiId}", apiId);
            
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Data = null,
                IsFromCache = false,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                Message = "Internal server error",
                Error = ex.Message
            });
        }
    }

    [HttpPost("schedule/{apiId}")]
    public async Task<IActionResult> ScheduleApiCall(string apiId, [FromBody] ScheduleRequest request)
    {
        try
        {
            _logger.LogInformation("Scheduling API call for ID: {ApiId} at {ScheduledTime}", apiId, request.ScheduledTime);

            var success = await _kafkaProducer.PublishScheduledApiCallAsync(apiId, request.ScheduledTime, request.Priority);
            
            if (success)
            {
                return Ok(new { Success = true, Message = "API call scheduled successfully", ApiId = apiId, ScheduledTime = request.ScheduledTime });
            }
            else
            {
                return StatusCode(500, new { Success = false, Message = "Failed to schedule API call" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling API call for ID: {ApiId}", apiId);
            return StatusCode(500, new { Success = false, Message = "Internal server error", Error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }

    private static bool IsDataFresh(ApiDataModel data, TimeSpan freshnessWindow)
    {
        return DateTime.UtcNow - data.LastUpdated <= freshnessWindow;
    }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public bool IsFromCache { get; set; }
    public DateTime LastUpdated { get; set; }
    public long ResponseTimeMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Warning { get; set; }
    public string? Error { get; set; }
}

public class ScheduleRequest
{
    public DateTime ScheduledTime { get; set; }
    public int Priority { get; set; } = 1;
}