using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class WishlistRepository(BookStoreDbContext db) : IWishlistRepository
{
    public async Task<IReadOnlyList<WishlistItem>> GetItemsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.WishlistItems
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Include(i => i.Book).ThenInclude(b => b.Authors)
            .OrderByDescending(i => i.AddedAt)
            .ThenBy(i => i.BookId)
            .ToListAsync(cancellationToken);

    public Task<WishlistItem?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken) =>
        db.WishlistItems.FirstOrDefaultAsync(i => i.UserId == userId && i.BookId == bookId, cancellationToken);

    public void Add(WishlistItem item) => db.WishlistItems.Add(item);

    public void Remove(WishlistItem item) => db.WishlistItems.Remove(item);
}
