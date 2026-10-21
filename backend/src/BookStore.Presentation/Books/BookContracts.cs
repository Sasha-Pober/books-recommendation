using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Entities;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Catalog;
using BookStore.Presentation.Common;
using BookStore.Presentation.Genres;

namespace BookStore.Presentation.Books;

public sealed record BookDto(
    Guid Id,
    string Title,
    string? Isbn,
    string? Annotation,
    string? Language,
    int? Year,
    int? Pages,
    string? CoverImageUrl,
    decimal Price,
    int Stock,
    IReadOnlyList<AuthorDto> Authors,
    IReadOnlyList<GenreDto> Genres,
    PublisherDto? Publisher,
    SeriesDto? Series,
    double? AverageRating,
    int RatingsCount);

public sealed record BookQuery : PageQuery
{
    /// <summary>Case-insensitive match on title or author name.</summary>
    [MaxLength(200)]
    public string? Search { get; init; }

    public Guid? GenreId { get; init; }

    public Guid? AuthorId { get; init; }
}

public sealed record UpsertBookRequest
{
    [Required, MaxLength(300)]
    public required string Title { get; init; }

    [MaxLength(20)]
    public string? Isbn { get; init; }

    [MaxLength(10000)]
    public string? Annotation { get; init; }

    [MaxLength(10)]
    public string? Language { get; init; }

    [Range(0, 2100)]
    public int? Year { get; init; }

    [Range(1, 100000)]
    public int? Pages { get; init; }

    [Url, MaxLength(2048)]
    public string? CoverImageUrl { get; init; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int Stock { get; init; }

    [Required, MinLength(1)]
    public required IReadOnlyList<Guid> AuthorIds { get; init; }

    public IReadOnlyList<Guid> GenreIds { get; init; } = [];

    public Guid? PublisherId { get; init; }

    public Guid? SeriesId { get; init; }
}

public sealed record RateBookRequest
{
    // TODO: take from the JWT once authentication is added.
    public required Guid UserId { get; init; }

    [Range(Rating.MinScore, Rating.MaxScore)]
    public int Score { get; init; }

    [MaxLength(4000)]
    public string? ReviewText { get; init; }
}
