namespace BookStore.Domain.Entities;

/// <summary>Key: (OrderId, BookId).</summary>
public class OrderItem
{
    public Guid OrderId { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>Unit price at the moment of purchase.</summary>
    public decimal Price { get; set; }
}
