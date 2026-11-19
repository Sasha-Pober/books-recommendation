using BookStore.Api.Auth;
using BookStore.Api.Infrastructure;
using BookStore.Domain.Exceptions;
using BookStore.Presentation.Auth;
using BookStore.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.Api.Controllers;

/// <summary>
/// Sign-in returns the access token in the body and sets the refresh token as an HttpOnly cookie
/// (<see cref="RefreshTokenCookie"/>); refresh and logout read the token from that cookie.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimits.Auth)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Creates a customer account. Guest events of the current browser session are attached to it.</summary>
    [HttpPost("register")]
    public async Task<SessionResponse> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request, cancellationToken);
        GuestSession.Renew(HttpContext);
        return StartSession(result);
    }

    /// <summary>Signs in. Guest events of the current browser session are attached to the account.</summary>
    [HttpPost("login")]
    public async Task<SessionResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);
        GuestSession.Renew(HttpContext);
        return StartSession(result);
    }

    /// <summary>Exchanges the refresh token cookie for a new access token (and a new refresh token cookie).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (RefreshTokenCookie.Read(HttpContext) is not { } token)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Not signed in.");
        }

        try
        {
            return StartSession(await auth.RefreshAsync(token, cancellationToken));
        }
        catch (UnauthorizedException exception)
        {
            // Expired, revoked or reused: the browser should stop sending it. Answered here rather than by the
            // exception handler, which clears response headers (and so this cookie deletion).
            RefreshTokenCookie.Delete(HttpContext);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: exception.Message);
        }
    }

    /// <summary>Revokes the refresh token and deletes its cookie. The access token stays valid until it expires (15 minutes by default).</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (RefreshTokenCookie.Read(HttpContext) is { } token)
        {
            await auth.LogoutAsync(token, cancellationToken);
        }

        RefreshTokenCookie.Delete(HttpContext);
        GuestSession.Renew(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [DisableRateLimiting]
    public Task<UserDto> Me(CancellationToken cancellationToken) => auth.GetCurrentUserAsync(cancellationToken);

    private SessionResponse StartSession(AuthResult result)
    {
        RefreshTokenCookie.Write(HttpContext, result.RefreshToken, result.RefreshTokenExpiresAt);
        return new SessionResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }
}
