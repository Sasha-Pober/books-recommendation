namespace BookStore.Domain.Entities;

/// <summary>Key: (UserId, BookId).</summary>
public class WishlistItem
{
    public Guid UserId { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
