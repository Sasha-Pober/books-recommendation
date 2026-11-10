using BookStore.Presentation.Genres;
using BookStore.Services.Genres;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/genres")]
public sealed class GenresController(IGenreService genres) : ControllerBase
{
    /// <summary>All genres, flat; build the tree from <c>parentId</c>.</summary>
    [HttpGet]
    public Task<IReadOnlyList<GenreDto>> GetAll(CancellationToken cancellationToken) =>
        genres.GetAllAsync(cancellationToken);
}
