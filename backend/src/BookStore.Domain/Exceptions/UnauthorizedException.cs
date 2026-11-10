namespace BookStore.Domain.Exceptions;

/// <summary>Authentication failed: wrong credentials, or an invalid / expired refresh token.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);
