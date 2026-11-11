using System.Text.Json;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Recommendations;

/// <summary>Collections in <see cref="IDistributedCache"/>: Redis when configured, process memory otherwise.</summary>
internal sealed class RecommendationCollectionCache(
    IDistributedCache cache,
    ILogger<RecommendationCollectionCache> logger) : IRecommendationCollectionCache
{
    /// <summary>
    /// Several refresh intervals, so the fallback keeps working on slightly stale data
    /// if the refresher fails for a while.
    /// </summary>
    internal static readonly TimeSpan TimeToLive = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ScoredBook>?> GetAsync(
        RecommendationCollection collection, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(collection), cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<List<ScoredBook>>(bytes, JsonOptions);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reading collection {Collection} from the cache failed", collection);
            return null;
        }
    }

    public async Task SetAsync(
        RecommendationCollection collection, IReadOnlyList<ScoredBook> items, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(
                Key(collection),
                JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeToLive },
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Writing collection {Collection} to the cache failed", collection);
        }
    }

    // Versioned, so a format change doesn't read old entries.
    private static string Key(RecommendationCollection collection) => $"recommendations:{collection}:v1";
}
