using BookStore.Domain.Common;
using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IOrderRepository
{
    /// <summary>Read-only; includes items with their books and authors. Newest first.</summary>
    Task<PagedList<Order>> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Read-only; includes items with their books and authors.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Order order);
}
