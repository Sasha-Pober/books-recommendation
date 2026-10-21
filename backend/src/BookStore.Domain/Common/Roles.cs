namespace BookStore.Domain.Common;

/// <summary>Roles of authenticated users. Guests are unauthenticated and have no role.</summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string Manager = "Manager";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Customer, Manager, Admin];
}
