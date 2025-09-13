using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace ApiTimeoutHandler.Services;

public class ExternalApiService : IExternalApiService
{
    private readonly HttpClient _httpClient;
    private readonly ExternalApiSettings _settings;
    private readonly ILogger<ExternalApiService> _logger;

    public ExternalApiService(HttpClient httpClient, IOptions<ExternalApiSettings> settings, ILogger<ExternalApiService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ApiTimeoutHandler/1.0");
    }

    public async Task<ApiCallResult> CallExternalApiAsync(string apiId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Calling external API for ID: {ApiId}", apiId);
            
            var url = $"{_settings.BaseUrl}/{_settings.EndpointPath}/{apiId}";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            stopwatch.Stop();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<object>(content);
                
                _logger.LogInformation("Successfully called external API for ID: {ApiId} in {ResponseTime}ms", 
                    apiId, stopwatch.ElapsedMilliseconds);
                
                return new ApiCallResult
                {
                    IsSuccess = true,
                    Data = data,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = (int)response.StatusCode
                };
            }
            else
            {
                _logger.LogWarning("External API call failed for ID: {ApiId} with status: {StatusCode}", 
                    apiId, response.StatusCode);
                
                return new ApiCallResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"API returned {response.StatusCode}: {content}",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = (int)response.StatusCode
                };
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("External API call timed out for ID: {ApiId} after {ResponseTime}ms", 
                apiId, stopwatch.ElapsedMilliseconds);
            
            return new ApiCallResult
            {
                IsSuccess = false,
                IsTimeout = true,
                ErrorMessage = "API call timed out",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                StatusCode = 408
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "HTTP error calling external API for ID: {ApiId}", apiId);
            
            return new ApiCallResult
            {
                IsSuccess = false,
                ErrorMessage = $"HTTP error: {ex.Message}",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                StatusCode = 500
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error calling external API for ID: {ApiId}", apiId);
            
            return new ApiCallResult
            {
                IsSuccess = false,
                ErrorMessage = $"Unexpected error: {ex.Message}",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                StatusCode = 500
            };
        }
    }

    public async Task<ApiCallResult> CallExternalApiWithRetryAsync(string apiId, int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        ApiCallResult? lastResult = null;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // Exponential backoff
                _logger.LogInformation("Retrying API call for ID: {ApiId}, attempt {Attempt} after {Delay}s delay", 
                    apiId, attempt, delay.TotalSeconds);
                
                await Task.Delay(delay, cancellationToken);
            }
            
            lastResult = await CallExternalApiAsync(apiId, cancellationToken);
            
            if (lastResult.IsSuccess || !ShouldRetry(lastResult))
            {
                break;
            }
        }
        
        return lastResult!;
    }

    private static bool ShouldRetry(ApiCallResult result)
    {
        // Retry on timeout, server errors, or network issues
        return result.IsTimeout || 
               result.StatusCode >= 500 || 
               result.StatusCode == 429 || // Too Many Requests
               result.StatusCode == 408;   // Request Timeout
    }
}

public class ExternalApiSettings
{
    public string BaseUrl { get; set; } = "https://api.example.com";
    public string EndpointPath { get; set; } = "data";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RateLimitDelayMs { get; set; } = 1000;
}