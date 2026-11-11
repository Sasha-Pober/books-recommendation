using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Identity;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Services.Recommendations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Extensions;

namespace BookStore.UnitTests.Recommendations;

public class RecommendationServiceTests
{
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IInteractionRepository _interactions = Substitute.For<IInteractionRepository>();
    private readonly IRecommendationLogRepository _logs = Substitute.For<IRecommendationLogRepository>();
    private readonly IRecommendationEngine _engine = Substitute.For<IRecommendationEngine>();
    private readonly IRecommendationCollectionCache _cache = Substitute.For<IRecommendationCollectionCache>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly List<RecommendationLog> _savedLogs = [];

    public RecommendationServiceTests()
    {
        _engine.Name.Returns("ml");
        _currentUser.SessionId.Returns("session-1");
        _ratings.GetSummariesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, RatingSummary>());
        _logs.SaveAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            var logs = call.Arg<IReadOnlyList<RecommendationLog>>();
            for (var i = 0; i < logs.Count; i++)
            {
                logs[i].Id = 100 + i;
            }

            _savedLogs.AddRange(logs);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Home_ForSignedInUser_SendsSessionAndExclusions_AndDropsExcludedBooksTheEngineReturns()
    {
        var userId = Guid.NewGuid();
        var purchased = NewBook("Куплена");
        var fresh = NewBook("Нова");
        _currentUser.UserId.Returns(userId);
        _interactions.GetPurchasedBookIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([purchased.Id]);
        _ratings.GetRatedBookIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        EngineReturns(new RecommendationResult(
            [new ScoredBook(purchased.Id, 0.9), new ScoredBook(fresh.Id, 0.8)], ModelVersionId: 3));
        InStock(purchased, fresh);

        var result = await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        await _engine.Received(1).RecommendAsync(
            Arg.Is<HomeRecommendationRequest>(r =>
                r.UserId == userId && r.SessionId == "session-1" && r.ExcludeBookIds.Contains(purchased.Id)),
            Arg.Any<CancellationToken>());
        Assert.Equal([fresh.Id], result.Items.Select(i => i.Book.Id));
    }

    [Fact]
    public async Task Home_KeepsEngineOrder_AddsExplanations_AndLogsImpressions()
    {
        var first = NewBook("Перша");
        var second = NewBook("Друга");
        var outOfStock = Guid.NewGuid();
        EngineReturns(new RecommendationResult(
            [
                new ScoredBook(first.Id, 0.9, RecommendationReason.SimilarToViewed),
                new ScoredBook(outOfStock, 0.8),
                new ScoredBook(second.Id, 0.7, RecommendationReason.LikedBySimilarReaders),
            ],
            ModelVersionId: 3));
        InStock(second, first);

        var result = await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        Assert.Equal("ml", result.Source);
        Assert.Equal(3, result.ModelVersionId);
        Assert.Equal([first.Id, second.Id], result.Items.Select(i => i.Book.Id));
        Assert.Equal("Схожа на книги, які ви переглядали", result.Items[0].Explanation);
        Assert.Equal([100L, 101L], result.Items.Select(i => i.LogId!.Value));

        Assert.Equal([0, 1], _savedLogs.Select(l => l.Position));
        Assert.All(_savedLogs, l =>
        {
            Assert.Equal(RecommendationPlacement.Home, l.Placement);
            Assert.Equal(3, l.ModelVersionId);
            Assert.Null(l.UserId);
        });
    }

    [Fact]
    public async Task Home_WhenLoggingFails_StillReturnsRecommendations()
    {
        var book = NewBook("Книга");
        EngineReturns(new RecommendationResult([new ScoredBook(book.Id, 1)]));
        InStock(book);
        _logs.Configure().SaveAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));

        var result = await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        Assert.Null(Assert.Single(result.Items).LogId);
    }

    [Fact]
    public async Task Home_WhenEngineFails_FallsBackToCachedCollections()
    {
        var bestseller = NewBook("Бестселер");
        var newRelease = NewBook("Новинка");
        _engine.RecommendAsync(default!, default).ThrowsAsyncForAnyArgs(new TimeoutException());
        _cache.GetAsync(RecommendationCollection.Bestsellers, Arg.Any<CancellationToken>())
            .Returns([new ScoredBook(bestseller.Id, 10, RecommendationReason.Bestseller)]);
        _cache.GetAsync(RecommendationCollection.NewReleases, Arg.Any<CancellationToken>())
            .Returns([new ScoredBook(newRelease.Id, 1, RecommendationReason.NewRelease)]);
        InStock(bestseller, newRelease);

        var result = await CreateService(_engine).GetHomeAsync(10, CancellationToken.None);

        Assert.Equal("baseline", result.Source);
        Assert.Null(result.ModelVersionId);
        Assert.Equal(["Бестселер", "Новинка"], result.Items.Select(i => i.Explanation));
        Assert.All(_savedLogs, l => Assert.Null(l.ModelVersionId));
    }

    [Fact]
    public async Task Similar_LogsWithSimilarBooksPlacement()
    {
        var book = NewBook("Дюна");
        var similar = NewBook("Месія Дюни");
        _books.ExistsAsync(book.Id, Arg.Any<CancellationToken>()).Returns(true);
        _engine.FindSimilarAsync(default, default, default!, default).ReturnsForAnyArgs(
            new RecommendationResult([new ScoredBook(similar.Id, 1, RecommendationReason.SameAuthor)]));
        InStock(similar);

        var result = await CreateService(_engine).GetSimilarAsync(book.Id, 5, CancellationToken.None);

        Assert.Equal("Того ж автора", Assert.Single(result.Items).Explanation);
        Assert.Equal(RecommendationPlacement.SimilarBooks, Assert.Single(_savedLogs).Placement);
    }

    private void EngineReturns(RecommendationResult result) =>
        _engine.RecommendAsync(default!, default).ReturnsForAnyArgs(result);

    private void InStock(params Book[] books) =>
        _books.GetInStockByIdsAsync(default!, default).ReturnsForAnyArgs(books);

    private RecommendationService CreateService(IRecommendationEngine engine) => new(
        _books,
        _ratings,
        _interactions,
        _logs,
        engine,
        new BaselineRecommendationEngine(
            _cache,
            new RecommendationCollectionBuilder(_orders, _interactions, _books, TimeProvider.System),
            _books,
            NullLogger<BaselineRecommendationEngine>.Instance),
        _currentUser,
        TimeProvider.System,
        NullLogger<RecommendationService>.Instance);

    private static Book NewBook(string title) => new()
    {
        Title = title,
        Stock = 1,
        Authors = [new Author { FullName = "Автор" }],
    };
}
