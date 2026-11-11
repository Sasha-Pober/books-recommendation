using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(BookStoreDbContext db) : IOrderRepository
{
    private IQueryable<Order> ReadOnlyWithItems =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Book).ThenInclude(b => b.Authors)
            .AsSplitQuery();

    public Task<PagedList<Order>> GetPageForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken) =>
        ReadOnlyWithItems
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToPagedListAsync(page, pageSize, cancellationToken);

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadOnlyWithItems.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScoredBook>> GetBestsellersAsync(
        DateTime since, int count, CancellationToken cancellationToken)
    {
        var sold = await db.OrderItems
            .Join(db.Orders, i => i.OrderId, o => o.Id, (i, o) => new { Item = i, Order = o })
            .Where(x => x.Order.CreatedAt >= since && x.Order.Status != OrderStatus.Cancelled && x.Item.Book.Stock > 0)
            .GroupBy(x => x.Item.BookId)
            .Select(g => new { BookId = g.Key, Sold = g.Sum(x => x.Item.Quantity) })
            .OrderByDescending(x => x.Sold)
            .ThenBy(x => x.BookId)
            .Take(count)
            .ToListAsync(cancellationToken);

        return sold.Select(x => new ScoredBook(x.BookId, x.Sold)).ToList();
    }

    public void Add(Order order) => db.Orders.Add(order);
}
