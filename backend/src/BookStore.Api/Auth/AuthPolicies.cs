using BookStore.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace BookStore.Api.Auth;

public static class AuthPolicies
{
    public const string Customer = nameof(Customer);

    /// <summary>Catalog management. Admins can do everything managers can.</summary>
    public const string Manager = nameof(Manager);

    public const string Admin = nameof(Admin);

    public static void Add(AuthorizationOptions options)
    {
        options.AddPolicy(Customer, p => p.RequireRole(Roles.Customer));
        options.AddPolicy(Manager, p => p.RequireRole(Roles.Manager, Roles.Admin));
        options.AddPolicy(Admin, p => p.RequireRole(Roles.Admin));
    }
}
