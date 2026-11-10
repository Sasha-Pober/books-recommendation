using BookStore.Api.Infrastructure;
using BookStore.Presentation.Interactions;
using BookStore.Services.Interactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.Api.Controllers;

/// <summary>
/// Collects page-view events from guests and signed-in users. The user comes from the JWT (if any)
/// and the guest session from its cookie. Events are written to the database in the background.
/// </summary>
[ApiController]
[Route("api/events")]
[EnableRateLimiting(RateLimits.Events)]
public sealed class EventsController(IInteractionService interactions) : ControllerBase
{
    /// <summary>Accepts up to 100 events; 503 with Retry-After when the server is overloaded.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Record(RecordInteractionsRequest request)
    {
        interactions.Record(request);
        return Accepted();
    }
}
