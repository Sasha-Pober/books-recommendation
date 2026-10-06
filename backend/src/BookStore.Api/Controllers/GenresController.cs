using BookStore.Application.Genres;
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
    public async Task<ActionResult<GenreDto>> Create(CreateGenreRequest request, CancellationToken cancellationToken)
    {
        var genre = await genres.CreateAsync(request, cancellationToken);
        return Created($"/api/genres/{genre.Id}", genre);
    }
}
