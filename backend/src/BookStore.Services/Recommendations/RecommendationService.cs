using BookStore.Application.Abstractions;
using BookStore.Application.Books;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Recommendations;

public interface IRecommendationService
{
    Task<RecommendationListDto> GetForUserAsync(Guid userId, int count, CancellationToken cancellationToken);

    Task<RecommendationListDto> GetPopularAsync(int count, CancellationToken cancellationToken);

    Task<RecommendationListDto> GetSimilarAsync(Guid bookId, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Facade over the recommendation engines: applies business rules (exclusions, stock),
/// enriches ids with book data and falls back to the baseline if the primary engine fails.
/// </summary>
internal sealed class RecommendationService(
    IApplicationDbContext db,
    IRecommendationEngine engine,
    BaselineRecommendationEngine fallback,
    ILogger<RecommendationService> logger) : IRecommendationService
{
    // Ask engines for extra candidates because some get filtered out (e.g. out of stock).
    private const int CandidateMultiplier = 2;

    public async Task<RecommendationListDto> GetForUserAsync(Guid userId, int count, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            throw new NotFoundException(nameof(User), userId);
        }

        // Don't recommend books the user already bought or rated.
        var purchased = db.Interactions
            .Where(i => i.UserId == userId && i.Type == InteractionType.Purchase)
            .Select(i => i.BookId);
        var rated = db.Ratings.Where(r => r.UserId == userId).Select(r => r.BookId);
        var exclude = await purchased.Union(rated).ToListAsync(cancellationToken);

        return await RecommendAsync(
            (e, ct) => e.RecommendForUserAsync(userId, count * CandidateMultiplier, exclude, ct),
            count,
            cancellationToken);
    }

    public Task<RecommendationListDto> GetPopularAsync(int count, CancellationToken cancellationToken) =>
        RecommendAsync(
            (e, ct) => e.RecommendForUserAsync(null, count * CandidateMultiplier, [], ct),
            count,
            cancellationToken);

    public async Task<RecommendationListDto> GetSimilarAsync(Guid bookId, int count, CancellationToken cancellationToken)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        return await RecommendAsync(
            (e, ct) => e.FindSimilarAsync(bookId, count * CandidateMultiplier, [bookId], ct),
            count,
            cancellationToken);
    }

    private async Task<RecommendationListDto> RecommendAsync(
        Func<IRecommendationEngine, CancellationToken, Task<IReadOnlyList<ScoredBook>>> query,
        int count,
        CancellationToken cancellationToken)
    {
        var (source, candidates) = await QueryWithFallbackAsync(query, cancellationToken);

        var ids = candidates.Select(c => c.BookId).Distinct().ToList();
        var books = await db.Books
            .AsNoTracking()
            .Where(b => ids.Contains(b.Id) && b.Stock > 0)
            .Select(BookMappings.ToDto)
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        // Keep the engine's ranking order.
        var items = candidates
            .Where(c => books.ContainsKey(c.BookId))
            .DistinctBy(c => c.BookId)
            .Take(count)
            .Select(c => new RecommendedBookDto(books[c.BookId], c.Score))
            .ToList();

        return new RecommendationListDto(Guid.CreateVersion7(), source, items);
    }

    private async Task<(string Source, IReadOnlyList<ScoredBook> Candidates)> QueryWithFallbackAsync(
        Func<IRecommendationEngine, CancellationToken, Task<IReadOnlyList<ScoredBook>>> query,
        CancellationToken cancellationToken)
    {
        if (engine is BaselineRecommendationEngine)
        {
            return (engine.Name, await query(engine, cancellationToken));
        }

        try
        {
            var candidates = await query(engine, cancellationToken);
            if (candidates.Count > 0)
            {
                return (engine.Name, candidates);
            }

            logger.LogInformation("Engine {Engine} returned no candidates, using fallback", engine.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Engine {Engine} failed, using fallback", engine.Name);
        }

        return (fallback.Name, await query(fallback, cancellationToken));
    }
}
