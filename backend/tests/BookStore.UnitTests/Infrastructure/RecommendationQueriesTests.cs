using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence.Repositories;

namespace BookStore.UnitTests.Infrastructure;

// Runs on the EF InMemory provider for now; moves to PostgreSQL (Testcontainers) with the integration tests.
public class RecommendationQueriesTests
{
    [Fact]
    public async Task GetPopularBooks_RanksByWeightedInteractions_AndSkipsOutOfStock()
    {
        await using var db = TestData.CreateDbContext();
        var author = new Author { FullName = "Author" };
        var viewed = TestData.Book("Viewed", author);
        var purchased = TestData.Book("Purchased", author);
        var outOfStock = TestData.Book("Out of stock", author, stock: 0);
        db.Books.AddRange(viewed, purchased, outOfStock);
        db.Interactions.AddRange(TestData.Interactions(viewed, InteractionType.View, 3));
        db.Interactions.AddRange(TestData.Interactions(purchased, InteractionType.Purchase, 1));
        db.Interactions.AddRange(TestData.Interactions(outOfStock, InteractionType.Purchase, 5));
        await db.SaveChangesAsync();

        var result = await new InteractionRepository(db).GetPopularBooksAsync(
            DateTime.UtcNow.AddDays(-1), 10, [], CancellationToken.None);

        Assert.Equal([purchased.Id, viewed.Id], result.Select(r => r.BookId));
    }

    [Fact]
    public async Task FindSimilarByMetadata_PrefersSameAuthor_OverSharedGenreOnly()
    {
        await using var db = TestData.CreateDbContext();
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

        Assert.Equal([lotr.Id, earthsea.Id], result.Select(r => r.BookId));
    }
}
