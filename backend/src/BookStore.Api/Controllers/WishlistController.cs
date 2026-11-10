using BookStore.Api.Auth;
using BookStore.Presentation.Shopping;
using BookStore.Services.Shopping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>The current customer's wishlist. Every change returns the whole updated list.</summary>
[ApiController]
[Route("api/wishlist")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class WishlistController(IWishlistService wishlist) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WishlistItemDto>> Get(CancellationToken cancellationToken) =>
        wishlist.GetAsync(cancellationToken);

    [HttpPost]
    public Task<IReadOnlyList<WishlistItemDto>> Add(AddToWishlistRequest request, CancellationToken cancellationToken) =>
        wishlist.AddAsync(request, cancellationToken);

    [HttpDelete("{bookId:guid}")]
    public Task<IReadOnlyList<WishlistItemDto>> Remove(Guid bookId, CancellationToken cancellationToken) =>
        wishlist.RemoveAsync(bookId, cancellationToken);
}
