using BookStore.Api.Auth;
using BookStore.Presentation.Books;
using BookStore.Presentation.Common;
using BookStore.Services.Books;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksController(IBookService books) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<BookDto>> Search([FromQuery] BookQuery query, CancellationToken cancellationToken) =>
        books.SearchAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<BookDto> GetById(Guid id, CancellationToken cancellationToken) =>
        books.GetByIdAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Manager)]
    public async Task<ActionResult<BookDto>> Create(UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = await books.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = book.Id }, book);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Manager)]
    public Task<BookDto> Update(Guid id, UpsertBookRequest request, CancellationToken cancellationToken) =>
        books.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Manager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await books.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Creates or updates the current user's rating of the book.</summary>
    [HttpPut("{id:guid}/rating")]
    [Authorize(Policy = AuthPolicies.Customer)]
    public async Task<IActionResult> Rate(Guid id, RateBookRequest request, CancellationToken cancellationToken)
    {
        await books.RateAsync(id, request, cancellationToken);
        return NoContent();
    }
}
