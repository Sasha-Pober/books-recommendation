using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Users;

namespace BookStore.Services.Users;

// Temporary until authentication is added: users will then be created on registration.
public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
}

internal sealed class UserService(IUserRepository users, IUnitOfWork unitOfWork) : IUserService
{
    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), id);

        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.ExistsByEmailAsync(email, cancellationToken))
        {
            throw new ConflictException($"User with email '{email}' already exists.");
        }

        var user = new User { Email = email, DisplayName = request.DisplayName.Trim() };
        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.DisplayName);
}
