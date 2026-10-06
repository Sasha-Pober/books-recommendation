using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

/// <summary>Explicit feedback: a user's 1–5 star rating of a book.</summary>
public class Rating : Entity
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int Score { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
