using System.ComponentModel.DataAnnotations;

namespace BookStore.Presentation.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(200)] string FullName);

public sealed record LoginRequest(
    [Required, MaxLength(256)] string Email,
    [Required, MaxLength(128)] string Password);

public sealed record UserDto(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles);

/// <summary>Result of signing in or refreshing. The API sends the refresh token only as an HttpOnly cookie.</summary>
/// <param name="AccessToken">JWT, sent as <c>Authorization: Bearer ...</c>.</param>
/// <param name="RefreshToken">Single-use: every refresh returns a new one.</param>
public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

/// <summary>
/// Response body of sign-in, registration and refresh. The refresh token is not in it: it is set as an
/// HttpOnly cookie limited to <c>/api/auth</c>, so page scripts can't read it.
/// </summary>
/// <param name="AccessToken">JWT, sent as <c>Authorization: Bearer ...</c>. Keep it in memory only.</param>
public sealed record SessionResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserDto User);
