using BookStore.Application.Books;
using BookStore.Application.Common.Models;
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
    public async Task<ActionResult<BookDto>> Create(UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = await books.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = book.Id }, book);
    }

    [HttpPut("{id:guid}")]
    public Task<BookDto> Update(Guid id, UpsertBookRequest request, CancellationToken cancellationToken) =>
        books.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await books.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Creates or updates the user's rating of the book.</summary>
    [HttpPut("{id:guid}/rating")]
    public async Task<IActionResult> Rate(Guid id, RateBookRequest request, CancellationToken cancellationToken)
    {
        await books.RateAsync(id, request, cancellationToken);
        return NoContent();
    }
}
