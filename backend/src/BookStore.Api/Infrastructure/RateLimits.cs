using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.Api.Infrastructure;

public static class RateLimits
{
    /// <summary>Per-IP limit on sign-in endpoints, against password guessing across many accounts.</summary>
    public const string Auth = nameof(Auth);

    /// <summary>Per-IP limit on event collection, so one client can't fill the event queue.</summary>
    public const string Events = nameof(Events);

    public static void Add(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
        options.AddPolicy(Events, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    }
}
