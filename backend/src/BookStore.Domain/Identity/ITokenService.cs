namespace BookStore.Domain.Identity;

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public sealed record RefreshToken(string Token, DateTime ExpiresAt);

/// <summary>
/// Issues short-lived access tokens (JWT) and long-lived, rotating refresh tokens.
/// Refresh token changes are tracked by the unit of work and saved by the caller.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user);

    Task<RefreshToken> CreateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the token and returns its owner, or null if the token is unknown, expired or revoked.
    /// Reusing an already rotated token revokes every active token of its owner (likely theft).
    /// </summary>
    Task<Guid?> RedeemRefreshTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>Revokes the token if it is active; unknown tokens are ignored.</summary>
    Task RevokeRefreshTokenAsync(string token, CancellationToken cancellationToken);
}
