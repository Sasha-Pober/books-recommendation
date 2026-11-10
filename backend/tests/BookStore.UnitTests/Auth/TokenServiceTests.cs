using BookStore.Domain.Common;
using BookStore.Domain.Identity;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BookStore.UnitTests.Auth;

public class TokenServiceTests
{
    private readonly JwtOptions _options = new()
    {
        SigningKey = "test-signing-key-that-is-long-enough-0123456789",
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(14),
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AccessToken_IsValidJwt_WithUserClaims()
    {
        await using var db = TestData.CreateDbContext();
        var user = new UserAccount(Guid.NewGuid(), "a@b.c", "Reader", [Roles.Customer, Roles.Manager]);

        var token = CreateService(db).CreateAccessToken(user);

        // Lifetime is checked via ExpiresAt below: the token was issued at the fake time, not now.
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _options.CreateSigningKey(),
            LifetimeValidator = (_, _, _, _) => true,
        });
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(user.Id.ToString(), result.Claims[JwtClaims.Subject]);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddMinutes(15), token.ExpiresAt);

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal([Roles.Customer, Roles.Manager], jwt.GetPayloadValue<string[]>(JwtClaims.Role));
    }

    [Fact]
    public async Task RefreshToken_IsSingleUse()
    {
        await using var db = TestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(db);
        var refresh = await service.CreateRefreshTokenAsync(userId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(userId, await service.RedeemRefreshTokenAsync(refresh.Token, CancellationToken.None));
        await db.SaveChangesAsync();
        Assert.Null(await service.RedeemRefreshTokenAsync(refresh.Token, CancellationToken.None));
    }

    [Fact]
    public async Task ReusingRotatedToken_RevokesAllTokensOfTheUser()
    {
        await using var db = TestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(db);
        var first = await service.CreateRefreshTokenAsync(userId, CancellationToken.None);
        await db.SaveChangesAsync();
        await service.RedeemRefreshTokenAsync(first.Token, CancellationToken.None);
        var second = await service.CreateRefreshTokenAsync(userId, CancellationToken.None);
        await db.SaveChangesAsync();

        // The old token is presented again (e.g. by an attacker who stole it).
        Assert.Null(await service.RedeemRefreshTokenAsync(first.Token, CancellationToken.None));
        await db.SaveChangesAsync();

        Assert.Null(await service.RedeemRefreshTokenAsync(second.Token, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredRefreshToken_IsRejected()
    {
        await using var db = TestData.CreateDbContext();
        var service = CreateService(db);
        var refresh = await service.CreateRefreshTokenAsync(Guid.NewGuid(), CancellationToken.None);
        await db.SaveChangesAsync();

        _time.Advance(TimeSpan.FromDays(15));

        Assert.Null(await service.RedeemRefreshTokenAsync(refresh.Token, CancellationToken.None));
    }

    [Fact]
    public async Task OnlyTokenHash_IsStored()
    {
        await using var db = TestData.CreateDbContext();
        var refresh = await CreateService(db).CreateRefreshTokenAsync(Guid.NewGuid(), CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = Assert.Single(db.RefreshTokens);
        Assert.NotEqual(refresh.Token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    private TokenService CreateService(BookStoreDbContext db) => new(db, Options.Create(_options), _time);
}
