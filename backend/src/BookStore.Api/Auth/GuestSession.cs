namespace BookStore.Api.Auth;

/// <summary>
/// Browser session id kept in an HttpOnly cookie. Identifies guests' events; on sign-in they are
/// attached to the account. The id is renewed on sign-in and sign-out so that events made after
/// one person signs out are never attached to the next person who signs in on the same browser.
/// </summary>
public static class GuestSession
{
    public const string CookieName = "bookstore_session";

    private static readonly object ItemKey = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public static string Get(HttpContext context) =>
        context.Items[ItemKey] as string
        ?? throw new InvalidOperationException("Guest session middleware has not run for this request.");

    public static void Renew(HttpContext context) => Set(context, NewId());

    public static IApplicationBuilder UseGuestSession(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var id = context.Request.Cookies[CookieName];
            if (id is not null && IsValid(id))
            {
                context.Items[ItemKey] = id;
            }
            else
            {
                Set(context, NewId());
            }

            await next(context);
        });

    private static void Set(HttpContext context, string id)
    {
        context.Items[ItemKey] = id;
        context.Response.Cookies.Append(CookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = Lifetime,
            IsEssential = true,
        });
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");

    private static bool IsValid(string id) => Guid.TryParseExact(id, "N", out _);
}
