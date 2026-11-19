using BookStore.Api.Auth;
using BookStore.Presentation.Books;
using BookStore.Presentation.Common;
using BookStore.Presentation.Ratings;
using BookStore.Presentation.Recommendations;
using BookStore.Services.Books;
using BookStore.Services.Ratings;
using BookStore.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Public catalog. Management is in <see cref="AdminBooksController"/>.</summary>
[ApiController]
[Route("api/books")]
public sealed class BooksController(
    IBookService books,
    IRatingService ratings,
    IRecommendationService recommendations) : ControllerBase
{
    /// <summary>Search, filter, sort and page the catalog.</summary>
    [HttpGet]
    public Task<PagedResult<BookDto>> Search([FromQuery] BookQuery query, CancellationToken cancellationToken) =>
        books.SearchAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<BookDto> GetById(Guid id, CancellationToken cancellationToken) =>
        books.GetByIdAsync(id, cancellationToken);

    /// <summary>"Similar books" for the book page.</summary>
    [HttpGet("{id:guid}/similar")]
    public Task<RecommendationListDto> GetSimilar(
        Guid id, [FromQuery] RecommendationQuery query, CancellationToken cancellationToken) =>
        recommendations.GetSimilarAsync(id, query.Count, cancellationToken);

    /// <summary>Ratings and reviews of the book, newest first (<c>withTextOnly</c>: written reviews only).</summary>
    [HttpGet("{id:guid}/ratings")]
    public Task<PagedResult<ReviewDto>> GetRatings(
        Guid id, [FromQuery] ReviewQuery query, CancellationToken cancellationToken) =>
        ratings.GetReviewsAsync(id, query, cancellationToken);

    /// <summary>The current customer's rating of the book; 204 if they haven't rated it.</summary>
    [HttpGet("{id:guid}/ratings/mine")]
    [Authorize(Policy = AuthPolicies.Customer)]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetMyRating(Guid id, CancellationToken cancellationToken) =>
        await ratings.GetMineAsync(id, cancellationToken) is { } review ? Ok(review) : NoContent();

    /// <summary>Creates or replaces the current user's rating (1–5) and optional review.</summary>
    [HttpPost("{id:guid}/ratings")]
    [Authorize(Policy = AuthPolicies.Customer)]
    public Task<ReviewDto> Rate(Guid id, RateBookRequest request, CancellationToken cancellationToken) =>
        ratings.RateAsync(id, request, cancellationToken);
}
