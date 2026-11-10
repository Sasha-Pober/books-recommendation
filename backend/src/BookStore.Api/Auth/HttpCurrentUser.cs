using BookStore.Domain.Identity;
using BookStore.Infrastructure.Identity;

namespace BookStore.Api.Auth;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext Context =>
        accessor.HttpContext ?? throw new InvalidOperationException("No HTTP request in progress.");

    public Guid? UserId =>
        Context.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(Context.User.FindFirst(JwtClaims.Subject)?.Value, out var id)
            ? id
            : null;

    public string SessionId => GuestSession.Get(Context);
}
