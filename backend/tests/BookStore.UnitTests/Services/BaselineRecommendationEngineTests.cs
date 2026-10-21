using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Services.Recommendations;
using NSubstitute;

namespace BookStore.UnitTests.Services;

public class BaselineRecommendationEngineTests
{
    [Fact]
    public async Task RecommendForUser_PadsWithNewestBooks_WhenNotEnoughPopularOnes()
    {
        var books = Substitute.For<IBookRepository>();
        var interactions = Substitute.For<IInteractionRepository>();
        Guid popular = Guid.NewGuid(), newest = Guid.NewGuid(), excluded = Guid.NewGuid();
        interactions.GetPopularBooksAsync(default, default, default!, default)
            .ReturnsForAnyArgs([new ScoredBook(popular, 5)]);
        books.GetNewestInStockIdsAsync(default, default!, default).ReturnsForAnyArgs([newest]);

        var engine = new BaselineRecommendationEngine(books, interactions);
        var result = await engine.RecommendForUserAsync(null, 2, [excluded], CancellationToken.None);

        Assert.Equal([popular, newest], result.Select(r => r.BookId));
        await books.Received().GetNewestInStockIdsAsync(
            1,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(excluded) && ids.Contains(popular)),
            Arg.Any<CancellationToken>());
    }
}
