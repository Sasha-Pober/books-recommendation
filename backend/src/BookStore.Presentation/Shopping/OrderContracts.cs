using BookStore.Domain.Enums;
using BookStore.Presentation.Books;

namespace BookStore.Presentation.Shopping;

/// <param name="UnitPrice">Price at the moment of purchase (the book's current price may differ).</param>
public sealed record OrderItemDto(BookSummaryDto Book, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(Guid Id, OrderStatus Status, decimal Total, DateTime CreatedAt, IReadOnlyList<OrderItemDto> Items);
