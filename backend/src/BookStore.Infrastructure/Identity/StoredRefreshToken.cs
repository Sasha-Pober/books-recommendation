namespace BookStore.Infrastructure.Identity;

/// <summary>Refresh token row. Only a hash of the token is stored, so a database leak doesn't expose usable tokens.</summary>
public sealed class StoredRefreshToken
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the token, lowercase hex.</summary>
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the token is used (rotated), logged out, or revoked after reuse detection.</summary>
    public DateTime? RevokedAt { get; set; }
}
