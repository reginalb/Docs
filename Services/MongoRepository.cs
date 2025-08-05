using ApiTimeoutHandler.Models;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace ApiTimeoutHandler.Services;

public class MongoRepository : IMongoRepository
{
    private readonly IMongoCollection<ApiDataModel> _collection;
    private readonly ILogger<MongoRepository> _logger;

    public MongoRepository(IOptions<MongoDbSettings> settings, ILogger<MongoRepository> logger)
    {
        _logger = logger;
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        _collection = database.GetCollection<ApiDataModel>(settings.Value.CollectionName);
        
        // Create index on apiId for faster lookups
        var indexKeys = Builders<ApiDataModel>.IndexKeys.Ascending(x => x.ApiId);
        var indexOptions = new CreateIndexOptions { Unique = true };
        var indexModel = new CreateIndexModel<ApiDataModel>(indexKeys, indexOptions);
        _collection.Indexes.CreateOneAsync(indexModel);
    }

    public async Task<ApiDataModel?> GetByApiIdAsync(string apiId)
    {
        try
        {
            var filter = Builders<ApiDataModel>.Filter.Eq(x => x.ApiId, apiId);
            var result = await _collection.Find(filter).FirstOrDefaultAsync();
            
            if (result != null)
            {
                _logger.LogInformation("Retrieved data for API ID: {ApiId}", apiId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data for API ID: {ApiId}", apiId);
            return null;
        }
    }

    public async Task<bool> UpsertAsync(ApiDataModel model)
    {
        try
        {
            var filter = Builders<ApiDataModel>.Filter.Eq(x => x.ApiId, model.ApiId);
            var options = new ReplaceOptions { IsUpsert = true };
            
            var result = await _collection.ReplaceOneAsync(filter, model, options);
            
            _logger.LogInformation("Upserted data for API ID: {ApiId}, Modified: {Modified}", 
                model.ApiId, result.ModifiedCount > 0 || result.UpsertedId != null);
            
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting data for API ID: {ApiId}", model.ApiId);
            return false;
        }
    }

    public async Task<bool> DeleteExpiredAsync()
    {
        try
        {
            var filter = Builders<ApiDataModel>.Filter.Lt(x => x.ExpiresAt, DateTime.UtcNow);
            var result = await _collection.DeleteManyAsync(filter);
            
            _logger.LogInformation("Deleted {Count} expired records", result.DeletedCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting expired records");
            return false;
        }
    }

    public async Task<List<ApiDataModel>> GetExpiredCacheAsync()
    {
        try
        {
            var filter = Builders<ApiDataModel>.Filter.Lt(x => x.ExpiresAt, DateTime.UtcNow);
            var result = await _collection.Find(filter).ToListAsync();
            
            _logger.LogInformation("Found {Count} expired cache entries", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving expired cache entries");
            return new List<ApiDataModel>();
        }
    }

    public async Task<bool> IsDataFreshAsync(string apiId, TimeSpan freshnessPeriod)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.Subtract(freshnessPeriod);
            var filter = Builders<ApiDataModel>.Filter.And(
                Builders<ApiDataModel>.Filter.Eq(x => x.ApiId, apiId),
                Builders<ApiDataModel>.Filter.Gte(x => x.LastUpdated, cutoffTime)
            );
            
            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking data freshness for API ID: {ApiId}", apiId);
            return false;
        }
    }
}

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "ApiTimeoutHandler";
    public string CollectionName { get; set; } = "ApiData";
}