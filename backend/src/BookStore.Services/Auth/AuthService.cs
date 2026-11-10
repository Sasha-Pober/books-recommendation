using BookStore.Domain.Common;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Auth;
using Microsoft.Extensions.Logging;

namespace BookStore.Services.Auth;

public interface IAuthService
{
    /// <summary>Creates a <see cref="Roles.Customer"/> account and signs it in.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for a new token pair (the old refresh token stops working).</summary>
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task<UserDto> GetCurrentUserAsync(CancellationToken cancellationToken);
}

internal sealed class AuthService(
    IIdentityService identity,
    ITokenService tokens,
    IInteractionRepository interactions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await identity.RegisterAsync(
            request.Email.Trim(), request.Password, request.FullName.Trim(), Roles.Customer, cancellationToken);

        return await SignInAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await identity.ValidateCredentialsAsync(request.Email.Trim(), request.Password, cancellationToken)
                   ?? throw new UnauthorizedException("Invalid email or password.");

        return await SignInAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = await tokens.RedeemRefreshTokenAsync(request.RefreshToken, cancellationToken);
        var user = userId is { } id ? await identity.FindByIdAsync(id, cancellationToken) : null;
        if (user is null)
        {
            // Persist revocations made while redeeming (e.g. after detecting token reuse).
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("The refresh token is invalid or expired.");
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await tokens.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } id ? await identity.FindByIdAsync(id, cancellationToken) : null;

        return user is null ? throw new UnauthorizedException("Not signed in.") : ToDto(user);
    }

    private async Task<AuthResponse> SignInAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var merged = await interactions.AssignGuestSessionAsync(currentUser.SessionId, user.Id, cancellationToken);
        if (merged > 0)
        {
            logger.LogInformation("Attached {Count} guest events to user {UserId}", merged, user.Id);
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = await tokens.CreateRefreshTokenAsync(user.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponse(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, ToDto(user));
    }

    private static UserDto ToDto(UserAccount user) => new(user.Id, user.Email, user.FullName, user.Roles);
}
