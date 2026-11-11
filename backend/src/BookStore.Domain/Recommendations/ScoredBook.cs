namespace BookStore.Domain.Recommendations;

/// <param name="Reason">Null when the engine gives no explanation.</param>
public sealed record ScoredBook(Guid BookId, double Score, RecommendationReason? Reason = null);
