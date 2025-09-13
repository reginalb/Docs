using ApiTimeoutHandler.Models;

namespace ApiTimeoutHandler.Services;

public interface IMongoRepository
{
    Task<ApiDataModel?> GetByApiIdAsync(string apiId);
    Task<bool> UpsertAsync(ApiDataModel model);
    Task<bool> DeleteExpiredAsync();
    Task<List<ApiDataModel>> GetExpiredCacheAsync();
    Task<bool> IsDataFreshAsync(string apiId, TimeSpan freshnessPeriod);
}