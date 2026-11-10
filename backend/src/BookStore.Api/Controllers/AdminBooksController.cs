using BookStore.Api.Auth;
using BookStore.Presentation.Books;
using BookStore.Services.Books;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/admin/books")]
[Authorize(Policy = AuthPolicies.Manager)]
public sealed class AdminBooksController(IBookService books) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookDto>> Create(UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = await books.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(BooksController.GetById), "Books", new { id = book.Id }, book);
    }

    [HttpPut("{id:guid}")]
    public Task<BookDto> Update(Guid id, UpsertBookRequest request, CancellationToken cancellationToken) =>
        books.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deletes a book that has never been ordered; otherwise 409 (set stock to 0 instead).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await books.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
