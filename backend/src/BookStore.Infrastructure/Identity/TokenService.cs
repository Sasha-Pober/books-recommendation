using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BookStore.Infrastructure.Identity;

internal sealed class TokenService(
    BookStoreDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(UserAccount user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now + _options.AccessTokenLifetime;

        var identity = new ClaimsIdentity(
        [
            new Claim(JwtClaims.Subject, user.Id.ToString()),
            new Claim(JwtClaims.Email, user.Email),
            new Claim(JwtClaims.Name, user.FullName),
            new Claim(JwtClaims.TokenId, Guid.NewGuid().ToString()),
            .. user.Roles.Select(role => new Claim(JwtClaims.Role, role)),
        ]);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = identity,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expiresAt);
    }

    public Task<RefreshToken> CreateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64));
        var stored = new StoredRefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + _options.RefreshTokenLifetime,
        };
        db.RefreshTokens.Add(stored);

        return Task.FromResult(new RefreshToken(token, stored.ExpiresAt));
    }

    public async Task<Guid?> RedeemRefreshTokenAsync(string token, CancellationToken cancellationToken)
    {
        var stored = await FindAsync(token, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (stored.RevokedAt is not null)
        {
            // An already used token is presented again: it was probably stolen. Sign the user out everywhere.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ForEachAsync(t => t.RevokedAt = now, cancellationToken);
            return null;
        }

        if (stored.ExpiresAt <= now)
        {
            return null;
        }

        stored.RevokedAt = now;
        return stored.UserId;
    }

    public async Task RevokeRefreshTokenAsync(string token, CancellationToken cancellationToken)
    {
        var stored = await FindAsync(token, cancellationToken);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    private Task<StoredRefreshToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        return db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
