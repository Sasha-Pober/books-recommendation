using BookStore.Api.Auth;
using BookStore.Api.Infrastructure;
using BookStore.Presentation.Auth;
using BookStore.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimits.Auth)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Creates a customer account. Guest events of the current browser session are attached to it.</summary>
    [HttpPost("register")]
    public async Task<AuthResponse> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.RegisterAsync(request, cancellationToken);
        GuestSession.Renew(HttpContext);
        return response;
    }

    /// <summary>Signs in. Guest events of the current browser session are attached to the account.</summary>
    [HttpPost("login")]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.LoginAsync(request, cancellationToken);
        GuestSession.Renew(HttpContext);
        return response;
    }

    /// <summary>Exchanges a refresh token for a new access + refresh token pair.</summary>
    [HttpPost("refresh")]
    public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        auth.RefreshAsync(request, cancellationToken);

    /// <summary>Revokes the refresh token. The access token stays valid until it expires (15 minutes by default).</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request, cancellationToken);
        GuestSession.Renew(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [DisableRateLimiting]
    public Task<UserDto> Me(CancellationToken cancellationToken) => auth.GetCurrentUserAsync(cancellationToken);
}
