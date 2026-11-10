using BookStore.Presentation.Catalog;
using BookStore.Services.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Publisher and series lists for catalog filters.</summary>
[ApiController]
[Route("api")]
public sealed class CatalogController(IPublisherService publishers, ISeriesService series) : ControllerBase
{
    [HttpGet("publishers")]
    public Task<IReadOnlyList<PublisherDto>> GetPublishers(CancellationToken cancellationToken) =>
        publishers.GetAllAsync(cancellationToken);

    [HttpGet("series")]
    public Task<IReadOnlyList<SeriesDto>> GetSeries(CancellationToken cancellationToken) =>
        series.GetAllAsync(cancellationToken);
}
