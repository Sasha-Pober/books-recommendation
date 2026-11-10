using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Common;
using BookStore.Presentation.Shopping;
using BookStore.Services.Books;
using BookStore.Services.Common;

namespace BookStore.Services.Shopping;

/// <summary>The current user's orders.</summary>
public interface IOrderService
{
    /// <summary>
    /// Turns the cart into an order: reserves stock, fixes prices, empties the cart and records purchase events,
    /// all in one transaction.
    /// </summary>
    Task<OrderDto> PlaceAsync(CancellationToken cancellationToken);

    Task<PagedResult<OrderDto>> GetMineAsync(PageQuery page, CancellationToken cancellationToken);

    /// <summary>Other users' orders are reported as not found.</summary>
    Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class OrderService(
    IOrderRepository orders,
    ICartRepository cart,
    IBookRepository books,
    IUserRepository users,
    IInteractionRepository interactions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IOrderService
{
    public async Task<OrderDto> PlaceAsync(CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var items = await cart.GetItemsAsync(userId, cancellationToken);
        if (items.Count == 0)
        {
            throw new ValidationException("The cart is empty.");
        }

        var orderId = await unitOfWork.InTransactionAsync(async ct =>
        {
            // A fixed lock order prevents deadlocks between concurrent orders for the same books.
            foreach (var item in items.OrderBy(i => i.BookId))
            {
                if (!await books.TryReserveStockAsync(item.BookId, item.Quantity, ct))
                {
                    throw new ConflictException(
                        $"Not enough copies of '{item.Book.Title}' in stock. Update the cart and try again.");
                }
            }

            var order = Order.Place(userId, items.Select(i => (i.BookId, i.Quantity, i.Book.Price)));
            orders.Add(order);
            foreach (var item in items)
            {
                cart.Remove(item);
            }

            interactions.AddRange(items.Select(i =>
                Interaction.Create(userId, currentUser.SessionId, i.BookId, InteractionType.Purchase)));

            await unitOfWork.SaveChangesAsync(ct);
            return order.Id;
        }, cancellationToken);

        return await GetByIdAsync(orderId, cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> GetMineAsync(PageQuery page, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var result = await orders.GetPageForUserAsync(userId, page.Page, page.PageSize, cancellationToken);

        return new PagedResult<OrderDto>(
            result.Items.Select(ToDto).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = await currentUser.RequireUserIdAsync(users, cancellationToken);
        var order = await orders.GetByIdAsync(id, cancellationToken);

        return order is not null && order.UserId == userId
            ? ToDto(order)
            : throw new NotFoundException(nameof(Order), id);
    }

    private static OrderDto ToDto(Order order) => new(
        order.Id,
        order.Status,
        order.Total,
        order.CreatedAt,
        order.Items
            .OrderBy(i => i.Book.Title)
            .Select(i => new OrderItemDto(i.Book.ToSummaryDto(), i.Quantity, i.Price, i.Price * i.Quantity))
            .ToList());
}
