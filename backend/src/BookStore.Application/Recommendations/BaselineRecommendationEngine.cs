using BookStore.Application.Abstractions;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Recommendations;

/// <summary>
/// Non-ML baseline: recent popularity for user recommendations and author/genre overlap
/// for similar books. Serves as the fallback when the ML service is unavailable and as a
/// baseline to compare the neural network against.
/// </summary>
public sealed class BaselineRecommendationEngine(IApplicationDbContext db) : IRecommendationEngine
{
    private static readonly TimeSpan PopularityWindow = TimeSpan.FromDays(90);

    public string Name => "baseline";

    public async Task<IReadOnlyList<ScoredBook>> RecommendForUserAsync(
        Guid? userId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken)
    {
        var excluded = excludeBookIds.ToList();
        var since = DateTime.UtcNow - PopularityWindow;

        var popular = await db.Interactions
            .Where(i => i.CreatedAt >= since && i.Book.Stock > 0 && !excluded.Contains(i.BookId))
            .GroupBy(i => i.BookId)
            .Select(g => new
            {
                BookId = g.Key,
                Score = g.Sum(i =>
                    i.Type == InteractionType.Purchase ? 5.0 :
                    i.Type == InteractionType.AddToCart ? 3.0 :
                    i.Type == InteractionType.AddToWishlist ? 2.0 :
                    1.0),
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToListAsync(cancellationToken);

        var result = popular.Select(x => new ScoredBook(x.BookId, x.Score)).ToList();
        if (result.Count >= count)
        {
            return result;
        }

        // Not enough interaction data yet (cold start): pad with the newest books.
        excluded.AddRange(result.Select(r => r.BookId));
        var newest = await db.Books
            .Where(b => b.Stock > 0 && !excluded.Contains(b.Id))
            .OrderByDescending(b => b.CreatedAt)
            .Take(count - result.Count)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        result.AddRange(newest.Select(id => new ScoredBook(id, 0)));
        return result;
    }

    public async Task<IReadOnlyList<ScoredBook>> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken)
    {
        var source = await db.Books
            .Where(b => b.Id == bookId)
            .Select(b => new { b.AuthorId, GenreIds = b.Genres.Select(g => g.Id).ToList() })
            .FirstOrDefaultAsync(cancellationToken);

        if (source is null)
        {
            return [];
        }

        var authorId = source.AuthorId;
        var genreIds = source.GenreIds;
        var excluded = excludeBookIds.Append(bookId).ToList();

        var similar = await db.Books
            .Where(b => b.Stock > 0 && !excluded.Contains(b.Id))
            .Where(b => b.AuthorId == authorId || b.Genres.Any(g => genreIds.Contains(g.Id)))
            .Select(b => new
            {
                b.Id,
                Score = (b.AuthorId == authorId ? 2.0 : 0.0) + b.Genres.Count(g => genreIds.Contains(g.Id)),
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToListAsync(cancellationToken);

        return similar.Select(x => new ScoredBook(x.Id, x.Score)).ToList();
    }
}
