using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Shopping;
using BookStore.Services.Books;
using BookStore.Services.Common;

namespace BookStore.Services.Shopping;

/// <summary>The current user's wishlist.</summary>
public interface IWishlistService
{
    Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent: adding a book that is already in the wishlist changes nothing.</summary>
    Task<IReadOnlyList<WishlistItemDto>> AddAsync(AddToWishlistRequest request, CancellationToken cancellationToken);

    /// <summary>Idempotent.</summary>
    Task<IReadOnlyList<WishlistItemDto>> RemoveAsync(Guid bookId, CancellationToken cancellationToken);
}

internal sealed class WishlistService(
    IWishlistRepository wishlist,
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IWishlistService
{
    public async Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<IReadOnlyList<WishlistItemDto>> AddAsync(
        AddToWishlistRequest request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        if (!await books.ExistsAsync(request.BookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), request.BookId);
        }

        if (await wishlist.GetAsync(userId, request.BookId, cancellationToken) is null)
        {
            wishlist.Add(new WishlistItem { UserId = userId, BookId = request.BookId });
            interactions.AddRange(
                [Interaction.Create(userId, currentUser.SessionId, request.BookId, InteractionType.AddToWishlist)]);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await LoadAsync(userId, cancellationToken);
    }

    public async Task<IReadOnlyList<WishlistItemDto>> RemoveAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        if (await wishlist.GetAsync(userId, bookId, cancellationToken) is { } item)
        {
            wishlist.Remove(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await LoadAsync(userId, cancellationToken);
    }

    private async Task<IReadOnlyList<WishlistItemDto>> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var items = await wishlist.GetItemsAsync(userId, cancellationToken);
        return items.Select(i => new WishlistItemDto(i.Book.ToSummaryDto(), i.AddedAt)).ToList();
    }
}
