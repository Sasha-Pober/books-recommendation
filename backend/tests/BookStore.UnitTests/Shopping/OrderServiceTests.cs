using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Services.Shopping;
using NSubstitute;

namespace BookStore.UnitTests.Shopping;

public class OrderServiceTests : ShoppingTestBase
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICartRepository _cart = Substitute.For<ICartRepository>();
    private readonly List<Order> _addedOrders = [];

    public OrderServiceTests()
    {
        // Run the transaction body directly.
        UnitOfWork.InTransactionAsync(Arg.Any<Func<CancellationToken, Task<Guid>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Guid>>>()(CancellationToken.None));
        _orders.When(o => o.Add(Arg.Any<Order>())).Do(call => _addedOrders.Add(call.Arg<Order>()));
    }

    [Fact]
    public async Task Place_TurnsCartIntoOrder_AtCurrentPrices()
    {
        var dune = NewBook("Дюна", price: 380);
        var hobbit = NewBook("Гобіт", price: 350);
        var items = CartWith((dune, 2), (hobbit, 1));
        Books.TryReserveStockAsync(default, default, default).ReturnsForAnyArgs(true);

        var order = await CreateService().PlaceAsync(CancellationToken.None);

        Assert.Equal(380 * 2 + 350, order.Total);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Contains(order.Items, i => i.Book.Id == dune.Id && i.Quantity == 2 && i.UnitPrice == 380);
        foreach (var item in items)
        {
            _cart.Received(1).Remove(item);
        }

        Assert.All(RecordedInteractions, i => Assert.Equal(InteractionType.Purchase, i.EventType));
        Assert.Equal(new[] { dune.Id, hobbit.Id }.Order(), RecordedInteractions.Select(i => i.BookId).Order());
        await UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Place_ReservesStockInBookIdOrder()
    {
        var books = Enumerable.Range(0, 5).Select(i => NewBook($"Book {i}")).ToArray();
        CartWith(books.Select(b => (b, 1)).ToArray());
        var reserved = new List<Guid>();
        Books.TryReserveStockAsync(default, default, default).ReturnsForAnyArgs(call =>
        {
            reserved.Add(call.Arg<Guid>());
            return true;
        });

        await CreateService().PlaceAsync(CancellationToken.None);

        Assert.Equal(books.Select(b => b.Id).Order(), reserved);
    }

    [Fact]
    public async Task Place_WhenStockRunsOut_ThrowsConflict_AndSavesNothing()
    {
        var book = NewBook("Остання книга");
        CartWith((book, 3));
        Books.TryReserveStockAsync(book.Id, 3, Arg.Any<CancellationToken>()).Returns(false);

        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService().PlaceAsync(CancellationToken.None));

        Assert.Contains("Остання книга", error.Message);
        Assert.Empty(_addedOrders);
        Assert.Empty(RecordedInteractions);
        await UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Place_WithEmptyCart_ThrowsValidation()
    {
        CartWith();

        await Assert.ThrowsAsync<ValidationException>(() => CreateService().PlaceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetById_OfAnotherUsersOrder_ThrowsNotFound()
    {
        var order = Order.Place(Guid.NewGuid(), [(Guid.NewGuid(), 1, 100m)]);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetByIdAsync(order.Id, CancellationToken.None));
    }

    private IReadOnlyList<CartItem> CartWith(params (Book Book, int Quantity)[] lines)
    {
        var items = lines
            .Select(l => new CartItem { UserId = UserId, BookId = l.Book.Id, Book = l.Book, Quantity = l.Quantity })
            .ToList();
        _cart.GetItemsAsync(UserId, Arg.Any<CancellationToken>()).Returns(items);

        // Order items reference books by id only; give the read-back order its books.
        _orders.GetByIdAsync(default, default).ReturnsForAnyArgs(call =>
        {
            var order = _addedOrders.FirstOrDefault(o => o.Id == call.Arg<Guid>());
            foreach (var item in order?.Items ?? [])
            {
                item.Book = lines.First(l => l.Book.Id == item.BookId).Book;
            }

            return order;
        });
        return items;
    }

    private OrderService CreateService() =>
        new(_orders, _cart, Books, Users, Interactions, CurrentUser, UnitOfWork);
}
