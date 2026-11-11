using BookStore.Presentation.Recommendations;
using BookStore.Services.Recommendations;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public sealed class RecommendationsController(IRecommendationService recommendations) : ControllerBase
{
    /// <summary>
    /// "Recommended for you": personalized when signed in, based on the session's views for guests;
    /// bestsellers and new releases when the recommendation service is unavailable (<c>source</c> = "baseline").
    /// </summary>
    [HttpGet("home")]
    public Task<RecommendationListDto> Home(
        [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetHomeAsync(query.Count, cancellationToken);
}
