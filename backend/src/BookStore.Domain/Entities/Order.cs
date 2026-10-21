using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Entities;

public class Order : Entity
{
    public Guid UserId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public decimal Total { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];
}
