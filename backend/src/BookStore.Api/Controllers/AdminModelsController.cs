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

    /// <summary>
    /// Switches recommendations to an imported model version (models are trained offline and imported with
    /// <c>recsys import-model</c>). 404 if unknown, 503 if the recommendation service is unavailable.
    /// </summary>
    [HttpPost("{id:int}/activate")]
    public Task<ModelVersionDto> Activate(int id, CancellationToken cancellationToken) =>
        models.ActivateAsync(id, cancellationToken);
}
