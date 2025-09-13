namespace ApiTimeoutHandler.Services;

public interface IExternalApiService
{
    Task<ApiCallResult> CallExternalApiAsync(string apiId, CancellationToken cancellationToken = default);
    Task<ApiCallResult> CallExternalApiWithRetryAsync(string apiId, int maxRetries = 3, CancellationToken cancellationToken = default);
}

public class ApiCallResult
{
    public bool IsSuccess { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool IsTimeout { get; set; }
    public int StatusCode { get; set; }
}