using BookStore.Api.Auth;
using BookStore.Presentation.Common;
using BookStore.Presentation.Shopping;
using BookStore.Services.Shopping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Places an order from the current cart (409 if a book no longer has enough stock).</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Place(CancellationToken cancellationToken)
    {
        var order = await orders.PlaceAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>The current customer's orders, newest first.</summary>
    [HttpGet]
    public Task<PagedResult<OrderDto>> GetMine([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        orders.GetMineAsync(page, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OrderDto> GetById(Guid id, CancellationToken cancellationToken) =>
        orders.GetByIdAsync(id, cancellationToken);
}
