using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Services.Recommendations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookStore.UnitTests.Recommendations;

public class BaselineRecommendationEngineTests
{
    private readonly IRecommendationCollectionCache _cache = Substitute.For<IRecommendationCollectionCache>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInteractionRepository _interactions = Substitute.For<IInteractionRepository>();
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();

    [Fact]
    public async Task Home_InterleavesBestsellersAndNewReleases_WithoutDuplicatesOrExcludedBooks()
    {
        Guid b1 = Guid.NewGuid(), b2 = Guid.NewGuid(), n1 = Guid.NewGuid(), excluded = Guid.NewGuid();
        Cached(RecommendationCollection.Bestsellers, b1, excluded, b2);
        Cached(RecommendationCollection.NewReleases, n1, b1);

        var result = await CreateEngine().RecommendAsync(
            new HomeRecommendationRequest(null, "s", 10, [excluded]), CancellationToken.None);

        Assert.Equal([b1, n1, b2], result.Items.Select(i => i.BookId));
        Assert.Null(result.ModelVersionId);
    }

    [Fact]
    public async Task Home_OnCacheMiss_ComputesCollections_AndCachesThem()
    {
        Guid sold = Guid.NewGuid(), trending = Guid.NewGuid(), newest = Guid.NewGuid();
        _cache.GetAsync(default, default).ReturnsForAnyArgs((IReadOnlyList<ScoredBook>?)null);
        _orders.GetBestsellersAsync(default, default, default).ReturnsForAnyArgs([new ScoredBook(sold, 7)]);
        _interactions.GetPopularBooksAsync(default, default, default!, default)
            .ReturnsForAnyArgs([new ScoredBook(trending, 3)]);
        _books.GetNewestInStockIdsAsync(default, default!, default).ReturnsForAnyArgs([newest]);

        var result = await CreateEngine().RecommendAsync(
            new HomeRecommendationRequest(null, "s", 10, []), CancellationToken.None);

        Assert.Equal(
            [(sold, RecommendationReason.Bestseller), (newest, RecommendationReason.NewRelease), (trending, RecommendationReason.Trending)],
            result.Items.Select(i => (i.BookId, i.Reason!.Value)));
        await _cache.Received(1).SetAsync(RecommendationCollection.Bestsellers, Arg.Any<IReadOnlyList<ScoredBook>>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(RecommendationCollection.NewReleases, Arg.Any<IReadOnlyList<ScoredBook>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Home_OnCacheHit_DoesNotQueryTheDatabase()
    {
        Cached(RecommendationCollection.Bestsellers, Guid.NewGuid());
        Cached(RecommendationCollection.NewReleases, Guid.NewGuid());

        await CreateEngine().RecommendAsync(new HomeRecommendationRequest(null, "s", 10, []), CancellationToken.None);

        await _orders.DidNotReceiveWithAnyArgs().GetBestsellersAsync(default, default, default);
        await _books.DidNotReceiveWithAnyArgs().GetNewestInStockIdsAsync(default, default!, default);
    }

    private void Cached(RecommendationCollection collection, params Guid[] ids) =>
        _cache.GetAsync(collection, Arg.Any<CancellationToken>())
            .Returns(ids.Select(id => new ScoredBook(id, 1)).ToList());

    private BaselineRecommendationEngine CreateEngine() => new(
        _cache,
        new RecommendationCollectionBuilder(_orders, _interactions, _books, TimeProvider.System),
        _books,
        NullLogger<BaselineRecommendationEngine>.Instance);
}
