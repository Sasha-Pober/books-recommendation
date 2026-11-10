namespace BookStore.Domain.Recommendations;

public sealed record BookReview(
    Guid UserId, string ReviewerName, int Score, string? ReviewText, DateTime CreatedAt, DateTime UpdatedAt);
