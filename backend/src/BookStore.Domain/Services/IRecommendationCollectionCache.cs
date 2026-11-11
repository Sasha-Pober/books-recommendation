using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Services;

/// <summary>
/// Shared cache (Redis) of precomputed non-personalized collections. Best effort: a cache failure
/// is reported as a miss, so the store keeps working when the cache is down.
/// </summary>
public interface IRecommendationCollectionCache
{
    /// <summary>Null on a cache miss or cache failure.</summary>
    Task<IReadOnlyList<ScoredBook>?> GetAsync(RecommendationCollection collection, CancellationToken cancellationToken);

    /// <summary>Failures are logged, not thrown.</summary>
    Task SetAsync(
        RecommendationCollection collection, IReadOnlyList<ScoredBook> items, CancellationToken cancellationToken);
}
