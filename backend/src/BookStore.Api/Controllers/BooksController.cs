using BookStore.Api.Auth;
using BookStore.Presentation.Books;
using BookStore.Presentation.Common;
using BookStore.Services.Books;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

/// <summary>Public catalog. Management is in <see cref="AdminBooksController"/>.</summary>
[ApiController]
[Route("api/books")]
public sealed class BooksController(IBookService books) : ControllerBase
{
    /// <summary>Search, filter, sort and page the catalog.</summary>
    [HttpGet]
    public Task<PagedResult<BookDto>> Search([FromQuery] BookQuery query, CancellationToken cancellationToken) =>
        books.SearchAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<BookDto> GetById(Guid id, CancellationToken cancellationToken) =>
        books.GetByIdAsync(id, cancellationToken);

    /// <summary>Creates or updates the current user's rating of the book.</summary>
    [HttpPut("{id:guid}/rating")]
    [Authorize(Policy = AuthPolicies.Customer)]
    public async Task<IActionResult> Rate(Guid id, RateBookRequest request, CancellationToken cancellationToken)
    {
        await books.RateAsync(id, request, cancellationToken);
        return NoContent();
    }
}
