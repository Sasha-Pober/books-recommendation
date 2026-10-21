using BookStore.Application.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Users;

// Temporary until authentication is added: users will then be created on registration.
public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
}

internal sealed class UserService(IApplicationDbContext db) : IUserService
{
    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(User), id);

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new ConflictException($"User with email '{email}' already exists.");
        }

        var user = new User { Email = email, DisplayName = request.DisplayName.Trim() };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new UserDto(user.Id, user.Email, user.DisplayName);
    }
}
