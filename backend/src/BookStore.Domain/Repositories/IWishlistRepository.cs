using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IWishlistRepository
{
    /// <summary>Read-only; includes each book with its authors. Newest first.</summary>
    Task<IReadOnlyList<WishlistItem>> GetItemsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<WishlistItem?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken);

    void Add(WishlistItem item);

    void Remove(WishlistItem item);
}
