using BookStore.Api.Auth;
using BookStore.Presentation.Genres;
using BookStore.Services.Genres;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/genres")]
public sealed class GenresController(IGenreService genres) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<GenreDto>> GetAll(CancellationToken cancellationToken) =>
        genres.GetAllAsync(cancellationToken);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Manager)]
    public async Task<ActionResult<GenreDto>> Create(CreateGenreRequest request, CancellationToken cancellationToken)
    {
        var genre = await genres.CreateAsync(request, cancellationToken);
        return Created($"/api/genres/{genre.Id}", genre);
    }
}
