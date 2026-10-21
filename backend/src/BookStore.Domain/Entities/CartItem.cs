namespace BookStore.Domain.Entities;

/// <summary>Key: (UserId, BookId). Persisted in the database so the cart survives restarts and devices.</summary>
public class CartItem
{
    public Guid UserId { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int Quantity { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
