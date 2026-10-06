using System.ComponentModel.DataAnnotations;
using BookStore.Application.Books;

namespace BookStore.Application.Recommendations;

public sealed record RecommendationQuery
{
    [Range(1, 50)]
    public int Count { get; init; } = 10;
}

public sealed record RecommendedBookDto(BookDto Book, double Score);

/// <param name="RecommendationId">
/// Sent back by the frontend with interaction events to attribute clicks to this list.
/// </param>
/// <param name="Source">Name of the engine that produced the list.</param>
public sealed record RecommendationListDto(
    Guid RecommendationId,
    string Source,
    IReadOnlyList<RecommendedBookDto> Items);
