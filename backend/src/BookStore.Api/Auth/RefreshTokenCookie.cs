namespace BookStore.Api.Auth;

/// <summary>
/// The refresh token travels only in this cookie: HttpOnly (page scripts, and so XSS, can't read it),
/// SameSite=Strict (other sites can't make the browser send it) and limited to <c>/api/auth</c>
/// (it isn't sent with any other request).
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "bookstore_refresh";
    public const string Path = "/api/auth";

    // Issued tokens are much shorter; anything longer is not ours.
    private const int MaxLength = 200;

    public static string? Read(HttpContext context) =>
        context.Request.Cookies[Name] is { Length: > 0 and <= MaxLength } token ? token : null;

    public static void Write(HttpContext context, string token, DateTime expiresAt) =>
        context.Response.Cookies.Append(Name, token, Options(context, expiresAt));

    public static void Delete(HttpContext context) => context.Response.Cookies.Delete(Name, Options(context, null));

    private static CookieOptions Options(HttpContext context, DateTime? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
