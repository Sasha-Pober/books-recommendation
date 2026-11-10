using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface ICartRepository
{
    /// <summary>Tracked; includes each book with its authors. Oldest first.</summary>
    Task<IReadOnlyList<CartItem>> GetItemsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<CartItem?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken);

    void Add(CartItem item);

    void Remove(CartItem item);
}
