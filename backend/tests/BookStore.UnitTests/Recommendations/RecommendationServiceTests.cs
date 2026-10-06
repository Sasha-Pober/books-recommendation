using BookStore.Application.Abstractions;
using BookStore.Application.Recommendations;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookStore.UnitTests.Recommendations;

public class RecommendationServiceTests
{
    [Fact]
    public async Task GetForUser_ExcludesPurchasedAndOutOfStockBooks()
    {
        await using var db = TestData.CreateDbContext();
        var user = new User { Email = "a@b.c", DisplayName = "A" };
        var author = new Author { FullName = "Author" };
        var bought = TestData.Book("Bought", author);
        var outOfStock = TestData.Book("Out of stock", author, stock: 0);
        var available = TestData.Book("Available", author);
        db.Users.Add(user);
        db.Books.AddRange(bought, outOfStock, available);
        db.Interactions.AddRange(TestData.Interactions(bought, InteractionType.Purchase, 1, user));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetForUserAsync(user.Id, 10, CancellationToken.None);

        Assert.Equal("baseline", result.Source);
        Assert.Equal([available.Id], result.Items.Select(i => i.Book.Id));
    }

    [Fact]
    public async Task GetPopular_FallsBackToBaseline_WhenPrimaryEngineFails()
    {
        await using var db = TestData.CreateDbContext();
        db.Books.Add(TestData.Book("Book", new Author { FullName = "Author" }));
        await db.SaveChangesAsync();

        var service = CreateService(db, new FailingEngine());
        var result = await service.GetPopularAsync(5, CancellationToken.None);

        Assert.Equal("baseline", result.Source);
        Assert.Single(result.Items);
    }

    private static RecommendationService CreateService(BookStoreDbContext db, IRecommendationEngine? engine = null)
    {
        var baseline = new BaselineRecommendationEngine(db);
        return new RecommendationService(
            db, engine ?? baseline, baseline, NullLogger<RecommendationService>.Instance);
    }

    private sealed class FailingEngine : IRecommendationEngine
    {
        public string Name => "failing";

        public Task<IReadOnlyList<ScoredBook>> RecommendForUserAsync(
            Guid? userId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken) =>
            throw new HttpRequestException("ML service unavailable");

        public Task<IReadOnlyList<ScoredBook>> FindSimilarAsync(
            Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken) =>
            throw new HttpRequestException("ML service unavailable");
    }
}
