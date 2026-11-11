using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using Microsoft.Extensions.Logging;

namespace BookStore.Services.Recommendations;

/// <summary>
/// Non-ML engine and the fallback when the recommendation service is unavailable:
/// "Recommended for you" interleaves bestsellers and new releases (precomputed, cached in Redis);
/// "Similar books" uses shared authors and genres.
/// </summary>
public sealed class BaselineRecommendationEngine(
    IRecommendationCollectionCache cache,
    RecommendationCollectionBuilder builder,
    IBookRepository books,
    ILogger<BaselineRecommendationEngine> logger) : IRecommendationEngine
{
    public string Name => "baseline";

    public async Task<RecommendationResult> RecommendAsync(
        HomeRecommendationRequest request, CancellationToken cancellationToken)
    {
        var bestsellers = await GetCollectionAsync(RecommendationCollection.Bestsellers, cancellationToken);
        var newReleases = await GetCollectionAsync(RecommendationCollection.NewReleases, cancellationToken);

        var excluded = request.ExcludeBookIds.ToHashSet();
        var items = Interleave(bestsellers, newReleases)
            .Where(b => excluded.Add(b.BookId))
            .Take(request.Count)
            .ToList();

        return new RecommendationResult(items);
    }

    public async Task<RecommendationResult> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken) =>
        new(await books.FindSimilarByMetadataAsync(bookId, count, excludeBookIds, cancellationToken));

    /// <summary>From the cache; on a miss (or cache outage) computed from the database and cached again.</summary>
    private async Task<IReadOnlyList<ScoredBook>> GetCollectionAsync(
        RecommendationCollection collection, CancellationToken cancellationToken)
    {
        if (await cache.GetAsync(collection, cancellationToken) is { } cached)
        {
            return cached;
        }

        logger.LogInformation("Collection {Collection} not cached; computing it", collection);
        var items = await builder.BuildAsync(collection, cancellationToken);
        await cache.SetAsync(collection, items, cancellationToken);
        return items;
    }

    private static IEnumerable<ScoredBook> Interleave(IReadOnlyList<ScoredBook> first, IReadOnlyList<ScoredBook> second)
    {
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                yield return first[i];
            }

            if (i < second.Count)
            {
                yield return second[i];
            }
        }
    }
}
