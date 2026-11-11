using BookStore.Api.Auth;
using BookStore.Presentation.Models;
using BookStore.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/admin/models")]
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class AdminModelsController(IModelService models) : ControllerBase
{
    /// <summary>Trained model versions with their offline metrics, newest first.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ModelVersionDto>> GetAll(CancellationToken cancellationToken) =>
        models.GetAllAsync(cancellationToken);

    /// <summary>Starts training in the recommendation service (409 if already running, 503 if unavailable).</summary>
    [HttpPost("retrain")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<TrainingJobDto>> Retrain(CancellationToken cancellationToken) =>
        Accepted(await models.RetrainAsync(cancellationToken));
}
