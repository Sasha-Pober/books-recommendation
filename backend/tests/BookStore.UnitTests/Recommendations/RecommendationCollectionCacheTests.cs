using BookStore.Domain.Recommendations;
using BookStore.Infrastructure.Recommendations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookStore.UnitTests.Recommendations;

public class RecommendationCollectionCacheTests
{
    [Fact]
    public async Task RoundTrips_BooksWithReasons()
    {
        var cache = new RecommendationCollectionCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<RecommendationCollectionCache>.Instance);
        var items = new[] { new ScoredBook(Guid.NewGuid(), 7, RecommendationReason.Bestseller) };

        await cache.SetAsync(RecommendationCollection.Bestsellers, items, CancellationToken.None);

        Assert.Equal(items, await cache.GetAsync(RecommendationCollection.Bestsellers, CancellationToken.None));
        Assert.Null(await cache.GetAsync(RecommendationCollection.NewReleases, CancellationToken.None));
    }

    [Fact]
    public async Task CacheOutage_IsAMiss_NotAnError()
    {
        var broken = Substitute.For<IDistributedCache>();
        broken.GetAsync(default!, default).ThrowsAsyncForAnyArgs(new TimeoutException("redis down"));
        broken.SetAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(new TimeoutException("redis down"));
        var cache = new RecommendationCollectionCache(broken, NullLogger<RecommendationCollectionCache>.Instance);

        Assert.Null(await cache.GetAsync(RecommendationCollection.Bestsellers, CancellationToken.None));
        await cache.SetAsync(RecommendationCollection.Bestsellers, [], CancellationToken.None);
    }
}
