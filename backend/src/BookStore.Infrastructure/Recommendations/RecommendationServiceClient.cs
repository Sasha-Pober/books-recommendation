using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Services;

namespace BookStore.Infrastructure.Recommendations;

/// <summary>
/// Client of the Python recommendation service (hybrid neural model). JSON in snake_case:
/// <code>
/// POST v1/recommendations/home    {"user_id", "session_id", "count", "exclude_book_ids"}
/// POST v1/recommendations/similar {"book_id", "count", "exclude_book_ids"}
/// → {"model_version_id": 3, "items": [{"book_id", "score", "reason"}]}
/// </code>
/// Timeout and circuit breaker are configured on the HttpClient (see DependencyInjection).
/// </summary>
internal sealed class RecommendationServiceClient(HttpClient http) : IRecommendationEngine
{
    public const string ServiceKeyHeader = "X-Service-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Name => "ml";

    public Task<RecommendationResult> RecommendAsync(
        HomeRecommendationRequest request, CancellationToken cancellationToken) =>
        PostAsync(
            "v1/recommendations/home",
            new HomeRequest(request.UserId, request.SessionId, request.Count, request.ExcludeBookIds),
            cancellationToken);

    public Task<RecommendationResult> FindSimilarAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeBookIds, CancellationToken cancellationToken) =>
        PostAsync("v1/recommendations/similar", new SimilarRequest(bookId, count, excludeBookIds), cancellationToken);

    private async Task<RecommendationResult> PostAsync<TRequest>(
        string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResponseBody>(JsonOptions, cancellationToken)
                      ?? throw new InvalidDataException("Empty response from the recommendation service.");

        var items = (payload.Items ?? [])
            .Where(i => i.BookId != Guid.Empty && double.IsFinite(i.Score))
            .Select(i => new ScoredBook(i.BookId, i.Score, ParseReason(i.Reason)))
            .ToList();
        return new RecommendationResult(items, payload.ModelVersionId);
    }

    /// <summary>"similar_to_viewed" → <see cref="RecommendationReason.SimilarToViewed"/>; unknown → null.</summary>
    internal static RecommendationReason? ParseReason(string? reason) =>
        reason is not null
        && !reason.Any(char.IsDigit)
        && Enum.TryParse<RecommendationReason>(reason.Replace("_", ""), ignoreCase: true, out var parsed)
            ? parsed
            : null;

    private sealed record HomeRequest(
        Guid? UserId, string SessionId, int Count, IReadOnlyCollection<Guid> ExcludeBookIds);

    private sealed record SimilarRequest(Guid BookId, int Count, IReadOnlyCollection<Guid> ExcludeBookIds);

    private sealed record ResponseBody(int? ModelVersionId, List<ResponseItem>? Items);

    private sealed record ResponseItem(Guid BookId, double Score, string? Reason);
}
