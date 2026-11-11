namespace BookStore.Domain.Recommendations;

/// <param name="Items">Ranked, best first.</param>
/// <param name="ModelVersionId">Model that produced the list; null for the non-ML baseline.</param>
public sealed record RecommendationResult(IReadOnlyList<ScoredBook> Items, int? ModelVersionId = null)
{
    public static readonly RecommendationResult Empty = new([]);
}
