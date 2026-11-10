using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Shopping;
using BookStore.Services.Books;
using BookStore.Services.Common;

namespace BookStore.Services.Shopping;

/// <summary>The current user's cart.</summary>
public interface ICartService
{
    Task<CartDto> GetAsync(CancellationToken cancellationToken);

    Task<CartDto> AddAsync(AddToCartRequest request, CancellationToken cancellationToken);

    Task<CartDto> UpdateQuantityAsync(Guid bookId, UpdateCartItemRequest request, CancellationToken cancellationToken);

    /// <summary>Idempotent: removing a book that isn't in the cart is not an error.</summary>
    Task<CartDto> RemoveAsync(Guid bookId, CancellationToken cancellationToken);

    Task<CartDto> ClearAsync(CancellationToken cancellationToken);
}

internal sealed class CartService(
    ICartRepository cart,
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : ICartService
{
    public async Task<CartDto> GetAsync(CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<CartDto> AddAsync(AddToCartRequest request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var book = await books.GetByIdAsync(request.BookId, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), request.BookId);

        var item = await cart.GetAsync(userId, request.BookId, cancellationToken);
        var quantity = (item?.Quantity ?? 0) + request.Quantity;
        EnsureAvailable(book, quantity);

        if (item is null)
        {
            cart.Add(new CartItem { UserId = userId, BookId = book.Id, Quantity = quantity });
        }
        else
        {
            item.Quantity = quantity;
        }

        interactions.AddRange([Interaction.Create(userId, currentUser.SessionId, book.Id, InteractionType.AddToCart)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<CartDto> UpdateQuantityAsync(
        Guid bookId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var item = await cart.GetAsync(userId, bookId, cancellationToken)
                   ?? throw new NotFoundException("Cart item", bookId);
        var book = await books.GetByIdAsync(bookId, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), bookId);

        EnsureAvailable(book, request.Quantity);
        item.Quantity = request.Quantity;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<CartDto> RemoveAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        if (await cart.GetAsync(userId, bookId, cancellationToken) is { } item)
        {
            cart.Remove(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<CartDto> ClearAsync(CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        foreach (var item in await cart.GetItemsAsync(userId, cancellationToken))
        {
            cart.Remove(item);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CartDto([], 0, 0);
    }

    private static void EnsureAvailable(Book book, int quantity)
    {
        if (quantity > CartLimits.MaxQuantity)
        {
            throw new ValidationException($"At most {CartLimits.MaxQuantity} copies of one book per order.");
        }

        if (quantity > book.Stock)
        {
            throw new ConflictException(book.Stock == 0
                ? $"'{book.Title}' is out of stock."
                : $"Only {book.Stock} copies of '{book.Title}' are in stock.");
        }
    }

    private async Task<CartDto> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var items = await cart.GetItemsAsync(userId, cancellationToken);
        var lines = items
            .Select(i => new CartItemDto(i.Book.ToSummaryDto(), i.Quantity, i.Book.Price * i.Quantity))
            .ToList();

        return new CartDto(lines, lines.Sum(l => l.Quantity), lines.Sum(l => l.LineTotal));
    }
}
