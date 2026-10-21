using BookStore.Presentation.Users;
using BookStore.Services.Users;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers;

// Temporary until authentication (registration / login) is added.
[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<UserDto> GetById(Guid id, CancellationToken cancellationToken) =>
        users.GetByIdAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }
}
