using BookStore.Application.Recommendations;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public sealed class RecommendationsController(IRecommendationService recommendations) : ControllerBase
{
    /// <summary>Personalized "Recommended for you" list.</summary>
    [HttpGet("users/{userId:guid}")]
    public Task<RecommendationListDto> ForUser(
        Guid userId, [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetForUserAsync(userId, query.Count, cancellationToken);

    /// <summary>Non-personalized list for anonymous visitors.</summary>
    [HttpGet("popular")]
    public Task<RecommendationListDto> Popular(
        [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetPopularAsync(query.Count, cancellationToken);

    /// <summary>"Similar books" shown on a book's page.</summary>
    [HttpGet("books/{bookId:guid}/similar")]
    public Task<RecommendationListDto> Similar(
        Guid bookId, [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetSimilarAsync(bookId, query.Count, cancellationToken);
}
