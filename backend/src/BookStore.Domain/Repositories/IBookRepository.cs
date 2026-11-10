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

    /// <summary>Ordered books can't be deleted: order history must keep pointing at them.</summary>
    Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetNewestInStockIdsAsync(
        int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    /// <summary>In-stock books sharing an author (weight 2) or genres (1 per shared genre).</summary>
    Task<IReadOnlyList<ScoredBook>> FindSimilarByMetadataAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    void Add(Book book);

    void Remove(Book book);
}
