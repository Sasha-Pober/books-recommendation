using BookStore.Domain.Entities;
using BookStore.Presentation.Ratings;
using BookStore.Services.Ratings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>The books' denormalized rating counters, kept by RatingService on PostgreSQL.</summary>
public class RatingCountersTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task RatingThroughTheService_KeepsTheBooksCountersInStepWithItsRatings()
    {
        Guid bookId, first, second;
        await using (var db = Fixture.CreateDbContext())
        {
            var book = TestData.Book("Книга", new Author { FullName = "Автор" });
            db.Books.Add(book);
            first = (await TestData.AddUserAsync(db, "first@example.com")).Id;
            second = (await TestData.AddUserAsync(db, "second@example.com")).Id;
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        await RateAsync(first, bookId, 2);
        await RateAsync(second, bookId, 5);
        await RateAsync(first, bookId, 4); // changed: 2 → 4

        await using var check = Fixture.CreateDbContext();
        var stored = await check.Books.SingleAsync(b => b.Id == bookId);
        var ratings = await check.Ratings.Where(r => r.BookId == bookId).ToListAsync();
        Assert.Equal((2, 9), (stored.RatingCount, stored.RatingSum));
        Assert.Equal((ratings.Count, ratings.Sum(r => r.Score)), (stored.RatingCount, stored.RatingSum));
        Assert.Equal(4.5, stored.AverageRating);
    }

    private async Task RateAsync(Guid userId, Guid bookId, int score)
    {
        await using var provider = TestServices.Build(Fixture.ConnectionString, userId);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRatingService>()
            .RateAsync(bookId, new RateBookRequest { Score = score }, CancellationToken.None);
    }
}
