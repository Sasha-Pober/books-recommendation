using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Infrastructure.Persistence.Repositories;
using BookStore.Services.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>Stock reservation and the order transaction, which mock-based tests can't verify.</summary>
public class OrderTransactionTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task ConcurrentReservations_NeverSellMoreThanTheStock()
    {
        Guid bookId;
        await using (var db = Fixture.CreateDbContext())
        {
            var book = TestData.Book("Остання книга", new Author { FullName = "Автор" }, stock: 3);
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        // Ten customers try to buy one copy at the same time, each with its own connection.
        var attempts = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = Fixture.CreateDbContext();
            return await new BookRepository(db).TryReserveStockAsync(bookId, 1, CancellationToken.None);
        });
        var results = await Task.WhenAll(attempts);

        Assert.Equal(3, results.Count(r => r));
        await using var check = Fixture.CreateDbContext();
        Assert.Equal(0, (await check.Books.SingleAsync(b => b.Id == bookId)).Stock);
    }

    [Fact]
    public async Task PlaceOrder_ReservesStock_FixesPrices_EmptiesCart_AndRecordsPurchases()
    {
        var (userId, first, second) = await SeedCartAsync(firstStock: 5, secondStock: 5);
        await using var provider = TestServices.Build(Fixture.ConnectionString, userId);
        await using var scope = provider.CreateAsyncScope();

        var order = await scope.ServiceProvider.GetRequiredService<IOrderService>().PlaceAsync(CancellationToken.None);

        Assert.Equal(2 * 100m + 1 * 250m, order.Total);
        await using var check = Fixture.CreateDbContext();
        Assert.Equal(3, (await check.Books.SingleAsync(b => b.Id == first)).Stock);
        Assert.Equal(4, (await check.Books.SingleAsync(b => b.Id == second)).Stock);
        Assert.Empty(check.CartItems);
        Assert.Equal(2, check.Interactions.Count(i => i.UserId == userId && i.EventType == InteractionType.Purchase));
    }

    [Fact]
    public async Task PlaceOrder_WhenABookRunsShort_RollsBackEverything()
    {
        // The short book is reserved last (books are locked in id order), so the other one
        // has already been decremented when the order fails: the rollback must undo it.
        var (userId, first, second) = await SeedCartAsync(firstStock: 5, secondStock: 5);
        var (available, shortBook) = first.CompareTo(second) < 0 ? (first, second) : (second, first);
        await using (var db = Fixture.CreateDbContext())
        {
            await db.Books.Where(b => b.Id == shortBook)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Stock, 0));
        }

        await using var provider = TestServices.Build(Fixture.ConnectionString, userId);
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<IOrderService>().PlaceAsync(CancellationToken.None));

        await using var check = Fixture.CreateDbContext();
        Assert.Equal(5, (await check.Books.SingleAsync(b => b.Id == available)).Stock);
        Assert.Empty(check.Orders);
        Assert.Equal(2, check.CartItems.Count());
        Assert.Empty(check.Interactions);
    }

    /// <summary>A customer with 2 copies of the first book (100) and 1 of the second (250) in the cart.</summary>
    private async Task<(Guid UserId, Guid First, Guid Second)> SeedCartAsync(int firstStock, int secondStock)
    {
        await using var db = Fixture.CreateDbContext();
        var user = await TestData.AddUserAsync(db);
        var author = new Author { FullName = "Автор" };
        var first = TestData.Book("Перша", author, stock: firstStock, price: 100);
        var second = TestData.Book("Друга", author, stock: secondStock, price: 250);
        db.Books.AddRange(first, second);
        db.CartItems.AddRange(
            new CartItem { UserId = user.Id, BookId = first.Id, Quantity = 2 },
            new CartItem { UserId = user.Id, BookId = second.Id, Quantity = 1 });
        await db.SaveChangesAsync();
        return (user.Id, first.Id, second.Id);
    }
}
