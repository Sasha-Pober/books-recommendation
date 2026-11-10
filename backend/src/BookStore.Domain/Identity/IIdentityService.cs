namespace BookStore.Domain.Identity;

/// <summary>User accounts and credentials (implemented with ASP.NET Core Identity).</summary>
public interface IIdentityService
{
    /// <exception cref="Exceptions.ConflictException">The email is already registered.</exception>
    /// <exception cref="Exceptions.ValidationException">The password doesn't meet the password policy.</exception>
    Task<UserAccount> RegisterAsync(
        string email, string password, string fullName, string role, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the account if the credentials are valid. Counts failed attempts;
    /// a locked-out account is rejected even with the right password.
    /// </summary>
    Task<UserAccount?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
