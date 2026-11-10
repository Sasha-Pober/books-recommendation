using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Enums;
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

public sealed record BookQuery : PageQuery, IValidatableObject
{
    /// <summary>
    /// Ukrainian full-text search over title and annotation (word forms are normalized, so «дюну» finds «Дюна»),
    /// plus substring match on title and author name. Supports web-search syntax: "exact phrase", -excluded, or.
    /// </summary>
    [MaxLength(200)]
    public string? Search { get; init; }

    /// <summary>Includes subgenres: «Фантастика» also returns «Фентезі».</summary>
    public Guid? GenreId { get; init; }

    public Guid? AuthorId { get; init; }

    public Guid? PublisherId { get; init; }

    public Guid? SeriesId { get; init; }

    /// <summary>ISO 639-1 code, e.g. "uk".</summary>
    [MaxLength(10)]
    public string? Language { get; init; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? MaxPrice { get; init; }

    [Range(0, 2100)]
    public int? MinYear { get; init; }

    [Range(0, 2100)]
    public int? MaxYear { get; init; }

    public bool InStockOnly { get; init; }

    /// <summary>Default: relevance when searching, otherwise title.</summary>
    [EnumDataType(typeof(BookSort))]
    public BookSort? Sort { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult(
                "MinPrice must not exceed MaxPrice.", [nameof(MinPrice), nameof(MaxPrice)]);
        }

        if (MinYear > MaxYear)
        {
            yield return new ValidationResult(
                "MinYear must not exceed MaxYear.", [nameof(MinYear), nameof(MaxYear)]);
        }
    }
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

/// <summary>Compact book for cart, wishlist and order lines.</summary>
public sealed record BookSummaryDto(
    Guid Id, string Title, IReadOnlyList<string> Authors, string? CoverImageUrl, decimal Price, int Stock);
