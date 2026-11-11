using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Recommendations;
using BookStore.Infrastructure.Persistence.Repositories;

namespace BookStore.IntegrationTests.Infrastructure;

public class RecommendationQueriesTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task GetPopularBooks_RanksByInteractionWeights_AndSkipsOutOfStock()
    {
        await using var db = Fixture.CreateDbContext();
        var author = new Author { FullName = "Author" };
        var viewed = TestData.Book("Viewed", author);
        var purchased = TestData.Book("Purchased", author);
        var outOfStock = TestData.Book("Out of stock", author, stock: 0);
        db.Books.AddRange(viewed, purchased, outOfStock);
        db.Interactions.AddRange(TestData.Interactions(viewed, InteractionType.View, 3));       // 3 × 0.2
        db.Interactions.AddRange(TestData.Interactions(purchased, InteractionType.Purchase, 1)); // 1 × 1.0
        db.Interactions.AddRange(TestData.Interactions(outOfStock, InteractionType.Purchase, 5));
        await db.SaveChangesAsync();

        var result = await new InteractionRepository(db).GetPopularBooksAsync(
            DateTime.UtcNow.AddDays(-1), 10, [], CancellationToken.None);

        Assert.Equal([purchased.Id, viewed.Id], result.Select(r => r.BookId));
    }

    [Fact]
    public async Task FindSimilarByMetadata_PrefersSameAuthor_OverSharedGenreOnly()
    {
        await using var db = Fixture.CreateDbContext();
        var fantasy = new Genre { Name = "Fantasy" };
        var tolkien = new Author { FullName = "Tolkien" };
        var other = new Author { FullName = "Other" };
        var hobbit = TestData.Book("The Hobbit", tolkien, genres: fantasy);
        var lotr = TestData.Book("LOTR", tolkien, genres: fantasy);
        var earthsea = TestData.Book("Earthsea", other, genres: fantasy);
        var unrelated = TestData.Book("Unrelated", other);
        db.Books.AddRange(hobbit, lotr, earthsea, unrelated);
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).FindSimilarByMetadataAsync(hobbit.Id, 10, [], CancellationToken.None);

        Assert.Equal(
            [(lotr.Id, RecommendationReason.SameAuthor), (earthsea.Id, RecommendationReason.SimilarContent)],
            result.Select(r => (r.BookId, r.Reason!.Value)));
    }

    [Fact]
    public async Task GetBestsellers_CountsCopiesSold_IgnoringCancelledAndOldOrders()
    {
        await using var db = Fixture.CreateDbContext();
        var user = await TestData.AddUserAsync(db);
        var author = new Author { FullName = "Author" };
        var popular = TestData.Book("Popular", author);
        var less = TestData.Book("Less", author);
        var cancelledOnly = TestData.Book("Cancelled", author);
        db.Books.AddRange(popular, less, cancelledOnly);
        var cancelled = Order.Place(user.Id, [(cancelledOnly.Id, 10, 1m)]);
        cancelled.Status = OrderStatus.Cancelled;
        db.Orders.AddRange(
            Order.Place(user.Id, [(popular.Id, 3, 1m), (less.Id, 1, 1m)]),
            Order.Place(user.Id, [(popular.Id, 1, 1m)]),
            cancelled);
        await db.SaveChangesAsync();

        var repository = new OrderRepository(db);
        var result = await repository.GetBestsellersAsync(DateTime.UtcNow.AddDays(-1), 10, CancellationToken.None);

        Assert.Equal([(popular.Id, 4.0), (less.Id, 1.0)], result.Select(r => (r.BookId, r.Score)));
        Assert.Empty(await repository.GetBestsellersAsync(DateTime.UtcNow.AddDays(1), 10, CancellationToken.None));
    }

    [Fact]
    public async Task AssignGuestSession_AttachesOnlyThatSessionsGuestEvents()
    {
        await using var db = Fixture.CreateDbContext();
        var user = await TestData.AddUserAsync(db);
        var book = TestData.Book("Book", new Author { FullName = "Author" });
        db.Books.Add(book);
        db.Interactions.AddRange(
            Interaction.Create(null, "mine", book.Id, InteractionType.View),
            Interaction.Create(null, "mine", book.Id, InteractionType.View),
            Interaction.Create(null, "someone-else", book.Id, InteractionType.View));
        await db.SaveChangesAsync();

        var attached = await new InteractionRepository(db).AssignGuestSessionAsync("mine", user.Id, CancellationToken.None);

        Assert.Equal(2, attached);
        await using var check = Fixture.CreateDbContext();
        Assert.Equal(2, check.Interactions.Count(i => i.UserId == user.Id));
        Assert.Null(check.Interactions.Single(i => i.SessionId == "someone-else").UserId);
    }
}
