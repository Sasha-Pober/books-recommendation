using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Recommendations;
using BookStore.Presentation.Books;

namespace BookStore.Presentation.Recommendations;

public sealed record RecommendationQuery
{
    [Range(1, 50)]
    public int Count { get; init; } = 10;
}

/// <param name="Explanation">Short Ukrainian text for the UI, e.g. «Схожа на книги, які ви переглядали».</param>
/// <param name="LogId">
/// Send it back as <c>recommendationLogId</c> with the View event when the user opens the book,
/// so the click is counted (CTR). Null if the impression couldn't be logged.
/// </param>
public sealed record RecommendedBookDto(
    BookDto Book, double Score, RecommendationReason? Reason, string? Explanation, long? LogId);

/// <param name="Source">Engine that produced the list: "ml" or "baseline" (fallback).</param>
/// <param name="ModelVersionId">Model version for ML results; null for the baseline.</param>
public sealed record RecommendationListDto(
    string Source,
    int? ModelVersionId,
    IReadOnlyList<RecommendedBookDto> Items);
