using BookStore.Domain.Common;
using BookStore.Domain.Entities;
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

    public void Add(Order order) => db.Orders.Add(order);
}
