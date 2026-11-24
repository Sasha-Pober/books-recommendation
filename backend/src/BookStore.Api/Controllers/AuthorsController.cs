using BookStore.Presentation.Authors;
using BookStore.Presentation.Common;
using BookStore.Services.Authors;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/authors")]
public sealed class AuthorsController(IAuthorService authors) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuthorDto>> GetAll([FromQuery] AuthorQuery query, CancellationToken cancellationToken) =>
        authors.GetAllAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AuthorDto> GetById(Guid id, CancellationToken cancellationToken) =>
        authors.GetByIdAsync(id, cancellationToken);
}
