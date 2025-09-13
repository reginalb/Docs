using ApiTimeoutHandler.Models;
using MongoDB.Bson;

namespace ApiTimeoutHandler.Services;

public class ScheduledApiCallService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledApiCallService> _logger;
    private readonly Timer _dailyTimer;
    private readonly Timer _eventProcessingTimer;

    public ScheduledApiCallService(IServiceProvider serviceProvider, ILogger<ScheduledApiCallService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Set up daily timer for 1 AM
        var now = DateTime.Now;
        var nextRun = now.Date.AddDays(1).AddHours(1); // Next 1 AM
        if (now.Hour < 1) // If it's before 1 AM today, run today
        {
            nextRun = now.Date.AddHours(1);
        }

        var timeUntilNextRun = nextRun - now;
        _dailyTimer = new Timer(async _ => await ExecuteDailyScheduledCalls(), null, timeUntilNextRun, TimeSpan.FromDays(1));

        // Set up event processing timer (every 30 seconds)
        _eventProcessingTimer = new Timer(async _ => await ProcessScheduledEvents(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled API Call Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Scheduled API Call Service stopped");
    }

    private async Task ExecuteDailyScheduledCalls()
    {
        _logger.LogInformation("Starting daily scheduled API calls at {Time}", DateTime.Now);

        using var scope = _serviceProvider.CreateScope();
        var kafkaProducer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();
        var mongoRepository = scope.ServiceProvider.GetRequiredService<IMongoRepository>();

        try
        {
            // Get list of API IDs that need to be called
            // This could come from configuration, database, or other sources
            var apiIds = await GetApiIdsToProcess();

            var batchSize = 10; // Process in batches to avoid overwhelming the system
            var delayBetweenBatches = TimeSpan.FromSeconds(30);

            for (int i = 0; i < apiIds.Count; i += batchSize)
            {
                var batch = apiIds.Skip(i).Take(batchSize);
                var tasks = new List<Task>();

                foreach (var apiId in batch)
                {
                    var scheduledTime = DateTime.UtcNow.AddSeconds(i / batchSize * 30); // Stagger the calls
                    tasks.Add(kafkaProducer.PublishScheduledApiCallAsync(apiId, scheduledTime));
                }

                await Task.WhenAll(tasks);
                
                _logger.LogInformation("Published {Count} scheduled events for batch starting at index {Index}", 
                    batch.Count(), i);

                if (i + batchSize < apiIds.Count)
                {
                    await Task.Delay(delayBetweenBatches);
                }
            }

            _logger.LogInformation("Completed daily scheduled API calls. Published events for {Count} API IDs", apiIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during daily scheduled API calls");
        }
    }

    private async Task ProcessScheduledEvents()
    {
        using var scope = _serviceProvider.CreateScope();
        var kafkaProducer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();
        var mongoRepository = scope.ServiceProvider.GetRequiredService<IMongoRepository>();
        var externalApiService = scope.ServiceProvider.GetRequiredService<IExternalApiService>();

        try
        {
            // This is a simplified version - in a real implementation, you'd consume from Kafka
            // For now, we'll process expired cache entries as a proxy
            var expiredEntries = await mongoRepository.GetExpiredCacheAsync();
            
            foreach (var entry in expiredEntries.Take(5)) // Limit to 5 at a time
            {
                await ProcessApiCall(entry.ApiId, externalApiService, mongoRepository, kafkaProducer);
                await Task.Delay(TimeSpan.FromSeconds(2)); // Rate limiting
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scheduled events");
        }
    }

    private async Task ProcessApiCall(string apiId, IExternalApiService externalApiService, 
        IMongoRepository mongoRepository, IKafkaProducer kafkaProducer)
    {
        try
        {
            _logger.LogInformation("Processing scheduled API call for ID: {ApiId}", apiId);

            var result = await externalApiService.CallExternalApiWithRetryAsync(apiId);
            
            var apiData = new ApiDataModel
            {
                ApiId = apiId,
                Data = result.Data != null ? BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result.Data)) : new BsonDocument(),
                LastUpdated = DateTime.UtcNow,
                IsFromCache = false,
                ApiCallSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                ResponseTimeMs = result.ResponseTimeMs,
                ExpiresAt = DateTime.UtcNow.AddHours(1) // Cache for 1 hour
            };

            await mongoRepository.UpsertAsync(apiData);

            if (!result.IsSuccess && result.IsTimeout)
            {
                // Schedule retry with exponential backoff
                await kafkaProducer.PublishRetryEventAsync(apiId, 1);
            }

            _logger.LogInformation("Completed processing API call for ID: {ApiId}, Success: {Success}", 
                apiId, result.IsSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API call for ID: {ApiId}", apiId);
        }
    }

    private async Task<List<string>> GetApiIdsToProcess()
    {
        // This is a placeholder - in a real implementation, you might:
        // 1. Read from a configuration file
        // 2. Query a database for active API IDs
        // 3. Call another service to get the list
        // 4. Read from environment variables

        await Task.CompletedTask; // Placeholder for async operation

        // For demo purposes, return a sample list
        return new List<string>
        {
            "api-001", "api-002", "api-003", "api-004", "api-005",
            "api-006", "api-007", "api-008", "api-009", "api-010"
        };
    }

    public override void Dispose()
    {
        _dailyTimer?.Dispose();
        _eventProcessingTimer?.Dispose();
        base.Dispose();
    }
}