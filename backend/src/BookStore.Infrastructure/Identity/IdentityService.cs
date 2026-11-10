using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<AppUser> userManager, BookStoreDbContext db) : IIdentityService
{
    public async Task<UserAccount> RegisterAsync(
        string email, string password, string fullName, string role, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException($"User with email '{email}' already exists.");
        }

        var user = new AppUser { UserName = email, Email = email, FullName = fullName };

        // User and role assignment succeed or fail together.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw ToException(created);
        }

        var roleAdded = await userManager.AddToRoleAsync(user, role);
        if (!roleAdded.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}': {string.Join("; ", roleAdded.Errors.Select(e => e.Description))}");
        }

        await transaction.CommitAsync(cancellationToken);

        return new UserAccount(user.Id, user.Email, user.FullName, [role]);
    }

    public async Task<UserAccount?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return await ToAccountAsync(user);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : await ToAccountAsync(user);
    }

    private async Task<UserAccount> ToAccountAsync(AppUser user) =>
        new(user.Id, user.Email!, user.FullName, [.. await userManager.GetRolesAsync(user)]);

    private static Exception ToException(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return new ConflictException("User with this email already exists.");
        }

        return new ValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
