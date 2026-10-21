using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;

namespace BookStore.Services.Recommendations;

/// <summary>
/// Non-ML baseline: recent popularity for user recommendations and author/genre overlap
/// for similar books. Serves as the fallback when the ML service is unavailable.
/// </summary>
public sealed class BaselineRecommendationEngine(
    IBookRepository books,
    IInteractionRepository interactions) : IRecommendationEngine
{
    private static readonly TimeSpan PopularityWindow = TimeSpan.FromDays(90);

    public string Name => "baseline";

    public async Task<IReadOnlyList<ScoredBook>> RecommendForUserAsync(
        Guid? userId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken)
    {
        var popular = await interactions.GetPopularBooksAsync(
            DateTime.UtcNow - PopularityWindow, count, excludeBookIds, cancellationToken);
        if (popular.Count >= count)
        {
            return popular;
        }

        // Not enough interaction data yet (cold start): pad with the newest books.
        var excluded = excludeBookIds.Concat(popular.Select(p => p.BookId)).ToList();
        var newest = await books.GetNewestInStockIdsAsync(count - popular.Count, excluded, cancellationToken);

        return [.. popular, .. newest.Select(id => new ScoredBook(id, 0))];
    }

    public Task<IReadOnlyList<ScoredBook>> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken) =>
        books.FindSimilarByMetadataAsync(bookId, count, excludeBookIds, cancellationToken);
}
