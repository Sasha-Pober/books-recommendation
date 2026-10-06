using BookStore.Application.Recommendations;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;

namespace BookStore.UnitTests.Recommendations;

public class BaselineRecommendationEngineTests
{
    [Fact]
    public async Task RecommendForUser_RanksByWeightedInteractions_AndPadsWithNewestBooks()
    {
        await using var db = TestData.CreateDbContext();
        var author = new Author { FullName = "Author" };
        var viewed = TestData.Book("Viewed", author);
        var purchased = TestData.Book("Purchased", author);
        var noInteractions = TestData.Book("New", author);
        db.Books.AddRange(viewed, purchased, noInteractions);
        db.Interactions.AddRange(TestData.Interactions(viewed, InteractionType.View, 3));
        db.Interactions.AddRange(TestData.Interactions(purchased, InteractionType.Purchase, 1));
        await db.SaveChangesAsync();

        var engine = new BaselineRecommendationEngine(db);
        var result = await engine.RecommendForUserAsync(null, 3, [], CancellationToken.None);

        Assert.Equal([purchased.Id, viewed.Id, noInteractions.Id], result.Select(r => r.BookId));
    }

    [Fact]
    public async Task FindSimilar_PrefersSameAuthor_OverSharedGenreOnly()
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

        var engine = new BaselineRecommendationEngine(db);
        var result = await engine.FindSimilarAsync(hobbit.Id, 10, [], CancellationToken.None);

        Assert.Equal([lotr.Id, earthsea.Id], result.Select(r => r.BookId));
    }
}
