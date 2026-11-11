using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Services;

/// <summary>
/// Produces ranked book ids with explanations. Implementations: the Python recommendation service client
/// and the built-in non-ML baseline (also the fallback).
/// </summary>
public interface IRecommendationEngine
{
    /// <summary>Reported to clients and logged, so results can be attributed to an engine.</summary>
    string Name { get; }

    /// <summary>"Recommended for you".</summary>
    Task<RecommendationResult> RecommendAsync(HomeRecommendationRequest request, CancellationToken cancellationToken);

    /// <summary>"Similar books" for a book page; the book itself is never returned.</summary>
    Task<RecommendationResult> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken);
}
