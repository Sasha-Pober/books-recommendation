using BookStore.Api.Auth;
using BookStore.Presentation.Shopping;
using BookStore.Services.Shopping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>The current customer's cart. Every change returns the whole updated cart.</summary>
[ApiController]
[Route("api/cart")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class CartController(ICartService cart) : ControllerBase
{
    [HttpGet]
    public Task<CartDto> Get(CancellationToken cancellationToken) => cart.GetAsync(cancellationToken);

    /// <summary>Adds copies (increases the quantity if the book is already in the cart).</summary>
    [HttpPost]
    public Task<CartDto> Add(AddToCartRequest request, CancellationToken cancellationToken) =>
        cart.AddAsync(request, cancellationToken);

    /// <summary>Sets the quantity of a book that is in the cart.</summary>
    [HttpPut("{bookId:guid}")]
    public Task<CartDto> UpdateQuantity(
        Guid bookId, UpdateCartItemRequest request, CancellationToken cancellationToken) =>
        cart.UpdateQuantityAsync(bookId, request, cancellationToken);

    [HttpDelete("{bookId:guid}")]
    public Task<CartDto> Remove(Guid bookId, CancellationToken cancellationToken) =>
        cart.RemoveAsync(bookId, cancellationToken);

    [HttpDelete]
    public Task<CartDto> Clear(CancellationToken cancellationToken) => cart.ClearAsync(cancellationToken);
}
