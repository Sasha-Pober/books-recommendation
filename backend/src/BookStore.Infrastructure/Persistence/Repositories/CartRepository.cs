using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class CartRepository(BookStoreDbContext db) : ICartRepository
{
    public async Task<IReadOnlyList<CartItem>> GetItemsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.CartItems
            .Where(i => i.UserId == userId)
            .Include(i => i.Book).ThenInclude(b => b.Authors)
            .OrderBy(i => i.AddedAt)
            .ThenBy(i => i.BookId)
            .ToListAsync(cancellationToken);

    public Task<CartItem?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken) =>
        db.CartItems.FirstOrDefaultAsync(i => i.UserId == userId && i.BookId == bookId, cancellationToken);

    public void Add(CartItem item) => db.CartItems.Add(item);

    public void Remove(CartItem item) => db.CartItems.Remove(item);
}
