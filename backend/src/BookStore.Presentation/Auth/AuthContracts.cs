using System.ComponentModel.DataAnnotations;

namespace BookStore.Presentation.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(200)] string FullName);

public sealed record LoginRequest(
    [Required, MaxLength(256)] string Email,
    [Required, MaxLength(128)] string Password);

public sealed record RefreshTokenRequest([Required, MaxLength(200)] string RefreshToken);

public sealed record UserDto(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles);

/// <param name="AccessToken">JWT, sent as <c>Authorization: Bearer ...</c>.</param>
/// <param name="RefreshToken">Single-use: every refresh returns a new one.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);
