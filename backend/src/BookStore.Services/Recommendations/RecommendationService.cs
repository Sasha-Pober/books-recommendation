using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Presentation.Recommendations;
using BookStore.Services.Books;
using Microsoft.Extensions.Logging;

namespace BookStore.Services.Recommendations;

public interface IRecommendationService
{
    /// <summary>"Recommended for you": personalized for signed-in users, session-based or popular for guests.</summary>
    Task<RecommendationListDto> GetHomeAsync(int count, CancellationToken cancellationToken);

    Task<RecommendationListDto> GetSimilarAsync(Guid bookId, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Facade over the recommendation engines: falls back to the baseline if the primary engine fails,
/// applies business rules (exclusions, stock), adds book data and explanations, and logs every
/// shown recommendation for CTR measurement.
/// </summary>
internal sealed class RecommendationService(
    IBookRepository books,
    IRatingRepository ratings,
    IInteractionRepository interactions,
    IRecommendationLogRepository logs,
    IRecommendationEngine engine,
    BaselineRecommendationEngine fallback,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<RecommendationService> logger) : IRecommendationService
{
    // Ask engines for extra candidates because some get filtered out (e.g. out of stock).
    private const int CandidateMultiplier = 2;

    public async Task<RecommendationListDto> GetHomeAsync(int count, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        IReadOnlyCollection<Guid> exclude = [];
        if (userId is { } id)
        {
            // Don't recommend books the user already bought or rated.
            var purchased = await interactions.GetPurchasedBookIdsAsync(id, cancellationToken);
            var rated = await ratings.GetRatedBookIdsAsync(id, cancellationToken);
            exclude = purchased.Union(rated).ToHashSet();
        }

        var request = new HomeRecommendationRequest(userId, currentUser.SessionId, count * CandidateMultiplier, exclude);
        return await BuildListAsync(
            RecommendationPlacement.Home,
            (e, ct) => e.RecommendAsync(request, ct),
            exclude,
            count,
            cancellationToken);
    }

    public async Task<RecommendationListDto> GetSimilarAsync(Guid bookId, int count, CancellationToken cancellationToken)
    {
        if (!await books.ExistsAsync(bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        return await BuildListAsync(
            RecommendationPlacement.SimilarBooks,
            (e, ct) => e.FindSimilarAsync(bookId, count * CandidateMultiplier, [bookId], ct),
            [bookId],
            count,
            cancellationToken);
    }

    private async Task<RecommendationListDto> BuildListAsync(
        RecommendationPlacement placement,
        Func<IRecommendationEngine, CancellationToken, Task<RecommendationResult>> query,
        IReadOnlyCollection<Guid> exclude,
        int count,
        CancellationToken cancellationToken)
    {
        var (source, result) = await QueryWithFallbackAsync(query, cancellationToken);

        var ids = result.Items.Select(c => c.BookId).Distinct().ToList();
        var inStock = (await books.GetInStockByIdsAsync(ids, cancellationToken)).ToDictionary(b => b.Id);

        // Keep the engine's ranking order; the engine should already have removed excluded books.
        var ranked = result.Items
            .Where(c => inStock.ContainsKey(c.BookId) && !exclude.Contains(c.BookId))
            .DistinctBy(c => c.BookId)
            .Take(count)
            .ToList();

        var logIds = await LogImpressionsAsync(placement, ranked, result.ModelVersionId, cancellationToken);
        var summaries = await ratings.GetSummariesAsync(ranked.Select(c => c.BookId).ToList(), cancellationToken);
        var items = ranked
            .Select((c, position) => new RecommendedBookDto(
                inStock[c.BookId].ToDto(summaries.For(c.BookId)),
                c.Score,
                c.Reason,
                RecommendationExplanations.For(c.Reason),
                logIds?[position]))
            .ToList();

        return new RecommendationListDto(source, result.ModelVersionId, items);
    }

    /// <summary>Returns log ids by position, or null if logging failed (recommendations are still returned).</summary>
    private async Task<IReadOnlyList<long>?> LogImpressionsAsync(
        RecommendationPlacement placement,
        IReadOnlyList<ScoredBook> ranked,
        int? modelVersionId,
        CancellationToken cancellationToken)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        var shownAt = timeProvider.GetUtcNow().UtcDateTime;
        var entries = ranked
            .Select((c, position) => new RecommendationLog
            {
                UserId = currentUser.UserId,
                BookId = c.BookId,
                Placement = placement,
                Position = position,
                ModelVersionId = modelVersionId,
                ShownAt = shownAt,
            })
            .ToList();

        try
        {
            await logs.SaveAsync(entries, cancellationToken);
            return entries.Select(e => e.Id).ToList();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to log {Count} recommendation impressions", entries.Count);
            return null;
        }
    }

    private async Task<(string Source, RecommendationResult Result)> QueryWithFallbackAsync(
        Func<IRecommendationEngine, CancellationToken, Task<RecommendationResult>> query,
        CancellationToken cancellationToken)
    {
        if (engine is BaselineRecommendationEngine)
        {
            return (engine.Name, await query(engine, cancellationToken));
        }

        try
        {
            var result = await query(engine, cancellationToken);
            if (result.Items.Count > 0)
            {
                return (engine.Name, result);
            }

            logger.LogInformation("Engine {Engine} returned no candidates, using fallback", engine.Name);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Engine {Engine} failed, using fallback", engine.Name);
        }

        return (fallback.Name, await query(fallback, cancellationToken));
    }
}
