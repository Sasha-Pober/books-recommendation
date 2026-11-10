using BookStore.Domain.Entities;
using BookStore.Domain.Identity;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Services.Recommendations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookStore.UnitTests.Services;

public class RecommendationServiceTests
{
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IInteractionRepository _interactions = Substitute.For<IInteractionRepository>();
    private readonly IRecommendationEngine _engine = Substitute.For<IRecommendationEngine>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public RecommendationServiceTests()
    {
        _engine.Name.Returns("ml");
        _ratings.GetSummariesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, RatingSummary>());
    }

    [Fact]
    public async Task GetHome_ForSignedInUser_ExcludesPurchasedAndRatedBooks()
    {
        var userId = Guid.NewGuid();
        Guid purchased = Guid.NewGuid(), rated = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _interactions.GetPurchasedBookIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([purchased]);
        _ratings.GetRatedBookIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([rated]);
        _engine.RecommendForUserAsync(default, default, default!, default).ReturnsForAnyArgs([]);
        _books.GetInStockByIdsAsync(default!, default).ReturnsForAnyArgs([]);

        await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        await _engine.Received().RecommendForUserAsync(
            userId,
            Arg.Any<int>(),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(purchased) && ids.Contains(rated)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHome_ForGuest_KeepsEngineOrder_AndDropsBooksThatAreNotInStock()
    {
        var first = NewBook("First");
        var second = NewBook("Second");
        var outOfStockId = Guid.NewGuid();
        _engine.RecommendForUserAsync(default, default, default!, default).ReturnsForAnyArgs(
            [new ScoredBook(first.Id, 3), new ScoredBook(outOfStockId, 2), new ScoredBook(second.Id, 1)]);
        _books.GetInStockByIdsAsync(default!, default).ReturnsForAnyArgs([second, first]);

        var result = await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        Assert.Equal("ml", result.Source);
        Assert.Equal([first.Id, second.Id], result.Items.Select(i => i.Book.Id));
    }

    [Fact]
    public async Task GetHome_FallsBackToBaseline_WhenPrimaryEngineFails()
    {
        var book = NewBook("Book");
        _engine.RecommendForUserAsync(default, default, default!, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("ML service unavailable"));
        _interactions.GetPopularBooksAsync(default, default, default!, default)
            .ReturnsForAnyArgs([new ScoredBook(book.Id, 1)]);
        _books.GetNewestInStockIdsAsync(default, default!, default).ReturnsForAnyArgs([]);
        _books.GetInStockByIdsAsync(default!, default).ReturnsForAnyArgs([book]);

        var result = await CreateService(_engine).GetHomeAsync(1, CancellationToken.None);

        Assert.Equal("baseline", result.Source);
        Assert.Equal([book.Id], result.Items.Select(i => i.Book.Id));
    }

    private RecommendationService CreateService(IRecommendationEngine engine) => new(
        _books,
        _ratings,
        _interactions,
        engine,
        new BaselineRecommendationEngine(_books, _interactions),
        _currentUser,
        NullLogger<RecommendationService>.Instance);

    private static Book NewBook(string title) => new()
    {
        Title = title,
        Stock = 1,
        Authors = [new Author { FullName = "Author" }],
    };
}
