namespace BookStore.Domain.Entities;

/// <summary>Explicit feedback: a user's 1–5 star rating of a book, optionally with a review. Key: (UserId, BookId).</summary>
public class Rating
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    public Guid UserId { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int Score { get; set; }

    public string? ReviewText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
