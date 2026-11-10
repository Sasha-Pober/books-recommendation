using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Shopping;
using BookStore.Services.Shopping;
using NSubstitute;

namespace BookStore.UnitTests.Shopping;

public class CartAndWishlistTests : ShoppingTestBase
{
    private readonly ICartRepository _cart = Substitute.For<ICartRepository>();
    private readonly IWishlistRepository _wishlist = Substitute.For<IWishlistRepository>();

    public CartAndWishlistTests()
    {
        _cart.GetItemsAsync(default, default).ReturnsForAnyArgs([]);
        _wishlist.GetItemsAsync(default, default).ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task AddToCart_IncreasesExistingQuantity_AndRecordsCartEvent()
    {
        var book = NewBook("Дюна", stock: 5);
        var item = new CartItem { UserId = UserId, BookId = book.Id, Quantity = 2 };
        Books.GetByIdAsync(book.Id, Arg.Any<CancellationToken>()).Returns(book);
        _cart.GetAsync(UserId, book.Id, Arg.Any<CancellationToken>()).Returns(item);

        await CartService().AddAsync(new AddToCartRequest { BookId = book.Id, Quantity = 3 }, CancellationToken.None);

        Assert.Equal(5, item.Quantity);
        var recorded = Assert.Single(RecordedInteractions);
        Assert.Equal(InteractionType.AddToCart, recorded.EventType);
        Assert.Equal(SessionId, recorded.SessionId);
        await UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToCart_MoreThanInStock_ThrowsConflict()
    {
        var book = NewBook("Дюна", stock: 2);
        Books.GetByIdAsync(book.Id, Arg.Any<CancellationToken>()).Returns(book);

        await Assert.ThrowsAsync<ConflictException>(() =>
            CartService().AddAsync(new AddToCartRequest { BookId = book.Id, Quantity = 3 }, CancellationToken.None));

        _cart.DidNotReceiveWithAnyArgs().Add(default!);
        Assert.Empty(RecordedInteractions);
    }

    [Fact]
    public async Task AddToCart_AsGuest_ThrowsUnauthorized()
    {
        CurrentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CartService().AddAsync(new AddToCartRequest { BookId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task AddToWishlist_Twice_RecordsOneEvent()
    {
        var bookId = Guid.NewGuid();
        Books.ExistsAsync(bookId, Arg.Any<CancellationToken>()).Returns(true);
        _wishlist.GetAsync(UserId, bookId, Arg.Any<CancellationToken>())
            .Returns(null, new WishlistItem { UserId = UserId, BookId = bookId });
        var service = WishlistService();

        await service.AddAsync(new AddToWishlistRequest(bookId), CancellationToken.None);
        await service.AddAsync(new AddToWishlistRequest(bookId), CancellationToken.None);

        _wishlist.Received(1).Add(Arg.Any<WishlistItem>());
        Assert.Equal(InteractionType.AddToWishlist, Assert.Single(RecordedInteractions).EventType);
    }

    private CartService CartService() => new(_cart, Books, Users, Interactions, CurrentUser, UnitOfWork);

    private WishlistService WishlistService() => new(_wishlist, Books, Users, Interactions, CurrentUser, UnitOfWork);
}
