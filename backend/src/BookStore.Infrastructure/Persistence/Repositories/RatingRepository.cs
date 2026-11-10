using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class RatingRepository(BookStoreDbContext db) : IRatingRepository
{
    public Task<Rating?> GetAsync(Guid userId, Guid bookId, CancellationToken cancellationToken) =>
        db.Ratings.FirstOrDefaultAsync(r => r.UserId == userId && r.BookId == bookId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetRatedBookIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Ratings.Where(r => r.UserId == userId).Select(r => r.BookId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, RatingSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
        {
            return new Dictionary<Guid, RatingSummary>();
        }

        return await db.Ratings
            .Where(r => bookIds.Contains(r.BookId))
            .GroupBy(r => r.BookId)
            .Select(g => new { BookId = g.Key, Average = g.Average(r => (double)r.Score), Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => new RatingSummary(x.Average, x.Count), cancellationToken);
    }

    public Task<PagedList<BookReview>> GetReviewsAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken) =>
        db.Ratings
            .Where(r => r.BookId == bookId)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { Rating = r, u.FullName })
            .OrderByDescending(x => x.Rating.UpdatedAt)
            .ThenBy(x => x.Rating.UserId)
            .Select(x => new BookReview(
                x.Rating.UserId, x.FullName, x.Rating.Score, x.Rating.ReviewText, x.Rating.CreatedAt, x.Rating.UpdatedAt))
            .ToPagedListAsync(page, pageSize, cancellationToken);

    public void Add(Rating rating) => db.Ratings.Add(rating);
}
