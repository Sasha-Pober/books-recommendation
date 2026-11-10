using System.ComponentModel.DataAnnotations;
using BookStore.Presentation.Books;

namespace BookStore.Presentation.Shopping;

public sealed record CartItemDto(BookSummaryDto Book, int Quantity, decimal LineTotal);

/// <param name="Total">At current prices; the order fixes the prices when placed.</param>
public sealed record CartDto(IReadOnlyList<CartItemDto> Items, int TotalQuantity, decimal Total);

/// <summary>Adds copies to the cart (increases the quantity if the book is already there).</summary>
public sealed record AddToCartRequest
{
    public required Guid BookId { get; init; }

    [Range(1, CartLimits.MaxQuantity)]
    public int Quantity { get; init; } = 1;
}

public sealed record UpdateCartItemRequest([Range(1, CartLimits.MaxQuantity)] int Quantity);

public static class CartLimits
{
    public const int MaxQuantity = 99;
}
