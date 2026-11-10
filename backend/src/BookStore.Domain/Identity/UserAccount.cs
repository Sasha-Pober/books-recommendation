namespace BookStore.Domain.Identity;

/// <summary>Read model of a user account managed by the identity provider.</summary>
public sealed record UserAccount(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles);
