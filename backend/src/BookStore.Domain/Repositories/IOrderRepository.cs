using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Repositories;

public interface IOrderRepository
{
    /// <summary>Read-only; includes items with their books and authors. Newest first.</summary>
    Task<PagedList<Order>> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Read-only; includes items with their books and authors.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>In-stock books ranked by copies sold since <paramref name="since"/> (cancelled orders excluded).</summary>
    Task<IReadOnlyList<ScoredBook>> GetBestsellersAsync(DateTime since, int count, CancellationToken cancellationToken);

    /// <summary>Time of the newest order, or null if there are none.</summary>
    Task<DateTime?> GetLatestOrderTimeAsync(CancellationToken cancellationToken);

    void Add(Order order);
}
