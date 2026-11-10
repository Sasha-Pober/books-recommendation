using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;

namespace BookStore.Services.Common;

internal static class CurrentUserExtensions
{
    /// <summary>The signed-in user's id; rejects guests and valid tokens of deleted accounts.</summary>
    public static async Task<Guid> RequireUserIdAsync(
        this ICurrentUser currentUser, IUserRepository users, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId && await users.ExistsAsync(userId, cancellationToken))
        {
            return userId;
        }

        throw new UnauthorizedException("Sign in to continue.");
    }
}
