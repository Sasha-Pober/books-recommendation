using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;

namespace BookStore.Services.Recommendations;

/// <summary>Computes the non-personalized collections from the database.</summary>
public sealed class RecommendationCollectionBuilder(
    IOrderRepository orders,
    IInteractionRepository interactions,
    IBookRepository books,
    TimeProvider timeProvider)
{
    /// <summary>Enough to fill a page after removing books the user already has.</summary>
    public const int CollectionSize = 100;

    private static readonly TimeSpan BestsellerWindow = TimeSpan.FromDays(90);
    private static readonly TimeSpan TrendingWindow = TimeSpan.FromDays(30);

    public Task<IReadOnlyList<ScoredBook>> BuildAsync(
        RecommendationCollection collection, CancellationToken cancellationToken) => collection switch
    {
        RecommendationCollection.Bestsellers => BuildBestsellersAsync(cancellationToken),
        RecommendationCollection.NewReleases => BuildNewReleasesAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, null),
    };

    private async Task<IReadOnlyList<ScoredBook>> BuildBestsellersAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sold = await orders.GetBestsellersAsync(now - BestsellerWindow, CollectionSize, cancellationToken);
        var result = sold.Select(b => b with { Reason = RecommendationReason.Bestseller }).ToList();
        if (result.Count >= CollectionSize)
        {
            return result;
        }

        // Few sales yet (new store): continue with books that are popular by views, cart and wishlist events.
        var trending = await interactions.GetPopularBooksAsync(
            now - TrendingWindow, CollectionSize - result.Count, result.Select(b => b.BookId).ToList(), cancellationToken);
        result.AddRange(trending.Select(b => b with { Reason = RecommendationReason.Trending }));
        return result;
    }

    private async Task<IReadOnlyList<ScoredBook>> BuildNewReleasesAsync(CancellationToken cancellationToken)
    {
        var newest = await books.GetNewestInStockIdsAsync(CollectionSize, [], cancellationToken);
        return newest
            .Select((id, index) => new ScoredBook(id, newest.Count - index, RecommendationReason.NewRelease))
            .ToList();
    }
}
