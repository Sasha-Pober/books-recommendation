using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Repositories;

public interface IBookRepository
{
    /// <summary>Read-only; includes authors, genres, publisher and series.</summary>
    Task<PagedList<Book>> SearchAsync(BookFilter filter, CancellationToken cancellationToken);

    /// <summary>Read-only; includes authors, genres, publisher and series.</summary>
    Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked for modification; includes authors and genres.</summary>
    Task<Book?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Read-only; includes authors, genres, publisher and series. Order is not preserved.</summary>
    Task<IReadOnlyList<Book>> GetInStockByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically decrements stock if at least <paramref name="quantity"/> is available. Runs immediately.
    /// Concurrent orders for the last copies can't both succeed.
    /// </summary>
    Task<bool> TryReserveStockAsync(Guid bookId, int quantity, CancellationToken cancellationToken);

    /// <summary>Ordered books can't be deleted: order history must keep pointing at them.</summary>
    Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetNewestInStockIdsAsync(
        int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    /// <summary>
    /// In-stock books sharing an author (weight 2, reason <see cref="RecommendationReason.SameAuthor"/>)
    /// or genres (1 per shared genre, reason <see cref="RecommendationReason.SimilarContent"/>).
    /// </summary>
    Task<IReadOnlyList<ScoredBook>> FindSimilarByMetadataAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    void Add(Book book);

    void Remove(Book book);
}
