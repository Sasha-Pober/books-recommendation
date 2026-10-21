using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Entities;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Common;
using BookStore.Presentation.Genres;

namespace BookStore.Presentation.Books;

public sealed record BookDto(
    Guid Id,
    string Title,
    string? Isbn,
    string? Description,
    int? PublicationYear,
    string? Language,
    string? CoverImageUrl,
    decimal Price,
    int Stock,
    AuthorDto Author,
    IReadOnlyList<GenreDto> Genres,
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
    public string? Description { get; init; }

    [Range(0, 2100)]
    public int? PublicationYear { get; init; }

    [MaxLength(10)]
    public string? Language { get; init; }

    [Url, MaxLength(2048)]
    public string? CoverImageUrl { get; init; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int Stock { get; init; }

    public required Guid AuthorId { get; init; }

    public IReadOnlyList<Guid> GenreIds { get; init; } = [];
}

public sealed record RateBookRequest
{
    // TODO: take from the JWT once authentication is added.
    public required Guid UserId { get; init; }

    [Range(Rating.MinScore, Rating.MaxScore)]
    public int Score { get; init; }
}
