using BookStore.Presentation.Recommendations;
using BookStore.Services.Recommendations;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public sealed class RecommendationsController(IRecommendationService recommendations) : ControllerBase
{
    /// <summary>"Recommended for you": personalized when signed in, popular books for guests.</summary>
    [HttpGet("home")]
    public Task<RecommendationListDto> Home(
        [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetHomeAsync(query.Count, cancellationToken);

    /// <summary>"Similar books" shown on a book's page.</summary>
    [HttpGet("books/{bookId:guid}/similar")]
    public Task<RecommendationListDto> Similar(
        Guid bookId, [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetSimilarAsync(bookId, query.Count, cancellationToken);
}
