using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Entities;

public class Order : Entity
{
    private Order()
    {
    }

    public Guid UserId { get; private set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public decimal Total { get; private set; }

    public ICollection<OrderItem> Items { get; private set; } = [];

    /// <summary>Creates an order; each line keeps the unit price at the moment of purchase.</summary>
    public static Order Place(Guid userId, IEnumerable<(Guid BookId, int Quantity, decimal UnitPrice)> lines)
    {
        var order = new Order { UserId = userId };
        foreach (var (bookId, quantity, unitPrice) in lines)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
            ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
            order.Items.Add(new OrderItem { OrderId = order.Id, BookId = bookId, Quantity = quantity, Price = unitPrice });
        }

        if (order.Items.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        order.Total = order.Items.Sum(i => i.Price * i.Quantity);
        return order;
    }
}
