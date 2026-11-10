namespace BookStore.Infrastructure.Identity;

/// <summary>Short JWT claim names, used without ASP.NET's mapping to long XML-namespace claim types.</summary>
public static class JwtClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string TokenId = "jti";
}
