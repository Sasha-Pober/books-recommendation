using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Presentation.Recommendations;
using BookStore.Services.Books;
using Microsoft.Extensions.Logging;

namespace BookStore.Services.Recommendations;

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
    IBookRepository books,
    IUserRepository users,
    IRatingRepository ratings,
    IInteractionRepository interactions,
    IRecommendationEngine engine,
    BaselineRecommendationEngine fallback,
    ILogger<RecommendationService> logger) : IRecommendationService
{
    // Ask engines for extra candidates because some get filtered out (e.g. out of stock).
    private const int CandidateMultiplier = 2;

    public async Task<RecommendationListDto> GetForUserAsync(Guid userId, int count, CancellationToken cancellationToken)
    {
        if (!await users.ExistsAsync(userId, cancellationToken))
        {
            throw new NotFoundException("User", userId);
        }

        // Don't recommend books the user already bought or rated.
        var purchased = await interactions.GetPurchasedBookIdsAsync(userId, cancellationToken);
        var rated = await ratings.GetRatedBookIdsAsync(userId, cancellationToken);
        var exclude = purchased.Union(rated).ToList();

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
        if (!await books.ExistsAsync(bookId, cancellationToken))
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
        var inStock = (await books.GetInStockByIdsAsync(ids, cancellationToken)).ToDictionary(b => b.Id);

        // Keep the engine's ranking order.
        var ranked = candidates
            .Where(c => inStock.ContainsKey(c.BookId))
            .DistinctBy(c => c.BookId)
            .Take(count)
            .ToList();

        var summaries = await ratings.GetSummariesAsync(ranked.Select(c => c.BookId).ToList(), cancellationToken);
        var items = ranked
            .Select(c => new RecommendedBookDto(inStock[c.BookId].ToDto(summaries.For(c.BookId)), c.Score))
            .ToList();

        return new RecommendationListDto(source, items);
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
