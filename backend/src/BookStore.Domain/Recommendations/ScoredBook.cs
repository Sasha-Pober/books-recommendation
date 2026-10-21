namespace BookStore.Domain.Recommendations;

public sealed record ScoredBook(Guid BookId, double Score);
