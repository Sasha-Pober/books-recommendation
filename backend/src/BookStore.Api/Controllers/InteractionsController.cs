using BookStore.Presentation.Interactions;
using BookStore.Services.Interactions;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Collects implicit feedback (views, clicks, purchases) from the frontend.</summary>
[ApiController]
[Route("api/interactions")]
public sealed class InteractionsController(IInteractionService interactions) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Record(RecordInteractionsRequest request, CancellationToken cancellationToken)
    {
        await interactions.RecordAsync(request, cancellationToken);
        return Accepted();
    }
}
