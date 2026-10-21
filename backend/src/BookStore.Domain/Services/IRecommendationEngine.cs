using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Services;

/// <summary>
/// Produces ranked book ids. Implementations: the built-in baseline (popularity / content)
/// and, later, a client of the Python recommendation service.
/// </summary>
public interface IRecommendationEngine
{
    /// <summary>Reported to clients and logged, so results can be attributed to a model.</summary>
    string Name { get; }

    /// <param name="userId">Null for anonymous visitors.</param>
    Task<IReadOnlyList<ScoredBook>> RecommendForUserAsync(
        Guid? userId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScoredBook>> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken);
}
