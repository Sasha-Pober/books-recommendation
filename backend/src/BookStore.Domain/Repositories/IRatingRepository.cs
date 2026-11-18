using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Repositories;

public interface IRatingRepository
{
    /// <summary>Tracked for modification.</summary>
    Task<Rating?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetRatedBookIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Ratings of the book, newest first, with the reviewer's name.</summary>
    Task<PagedList<BookReview>> GetReviewsAsync(Guid bookId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Rating rating);
}
