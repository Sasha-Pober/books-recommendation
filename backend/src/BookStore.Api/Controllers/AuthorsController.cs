using BookStore.Api.Auth;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Common;
using BookStore.Services.Authors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/authors")]
public sealed class AuthorsController(IAuthorService authors) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuthorDto>> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken) =>
        authors.GetAllAsync(page, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AuthorDto> GetById(Guid id, CancellationToken cancellationToken) =>
        authors.GetByIdAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Manager)]
    public async Task<ActionResult<AuthorDto>> Create(CreateAuthorRequest request, CancellationToken cancellationToken)
    {
        var author = await authors.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = author.Id }, author);
    }
}
