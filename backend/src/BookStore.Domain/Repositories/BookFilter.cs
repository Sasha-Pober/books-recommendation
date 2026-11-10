using BookStore.Domain.Enums;

namespace BookStore.Domain.Repositories;

/// <param name="Search">Full-text query over title and annotation, plus substring match on title and author names.</param>
/// <param name="GenreIds">Books in any of these genres. Callers expand a genre to its subgenres.</param>
/// <param name="Sort">Null: by relevance when searching, otherwise by title.</param>
public sealed record BookFilter(
    string? Search,
    IReadOnlyCollection<Guid>? GenreIds,
    Guid? AuthorId,
    Guid? PublisherId,
    Guid? SeriesId,
    string? Language,
    decimal? MinPrice,
    decimal? MaxPrice,
    int? MinYear,
    int? MaxYear,
    bool InStockOnly,
    BookSort? Sort,
    int Page,
    int PageSize);
