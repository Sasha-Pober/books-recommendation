using BookStore.Api.Auth;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Catalog;
using BookStore.Presentation.Genres;
using BookStore.Services.Authors;
using BookStore.Services.Catalog;
using BookStore.Services.Genres;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Reference data used when creating books.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.Manager)]
public sealed class AdminCatalogController(
    IAuthorService authors,
    IGenreService genres,
    IPublisherService publishers,
    ISeriesService series) : ControllerBase
{
    [HttpPost("authors")]
    public async Task<ActionResult<AuthorDto>> CreateAuthor(
        CreateAuthorRequest request, CancellationToken cancellationToken)
    {
        var author = await authors.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(AuthorsController.GetById), "Authors", new { id = author.Id }, author);
    }

    [HttpPost("genres")]
    public async Task<ActionResult<GenreDto>> CreateGenre(
        CreateGenreRequest request, CancellationToken cancellationToken) =>
        Created("/api/genres", await genres.CreateAsync(request, cancellationToken));

    [HttpPost("publishers")]
    public async Task<ActionResult<PublisherDto>> CreatePublisher(
        CreatePublisherRequest request, CancellationToken cancellationToken) =>
        Created("/api/publishers", await publishers.CreateAsync(request, cancellationToken));

    [HttpPost("series")]
    public async Task<ActionResult<SeriesDto>> CreateSeries(
        CreateSeriesRequest request, CancellationToken cancellationToken) =>
        Created("/api/series", await series.CreateAsync(request, cancellationToken));
}
