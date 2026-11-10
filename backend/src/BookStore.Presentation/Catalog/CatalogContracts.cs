using System.ComponentModel.DataAnnotations;

namespace BookStore.Presentation.Catalog;

public sealed record PublisherDto(Guid Id, string Name);

public sealed record SeriesDto(Guid Id, string Name);

public sealed record CreatePublisherRequest([Required, MaxLength(200)] string Name);

public sealed record CreateSeriesRequest([Required, MaxLength(200)] string Name);
