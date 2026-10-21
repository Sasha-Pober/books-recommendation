using System.ComponentModel.DataAnnotations;
using BookStore.Presentation.Books;

namespace BookStore.Presentation.Recommendations;

public sealed record RecommendationQuery
{
    [Range(1, 50)]
    public int Count { get; init; } = 10;
}

public sealed record RecommendedBookDto(BookDto Book, double Score);

/// <param name="Source">Name of the engine that produced the list.</param>
public sealed record RecommendationListDto(
    string Source,
    IReadOnlyList<RecommendedBookDto> Items);
