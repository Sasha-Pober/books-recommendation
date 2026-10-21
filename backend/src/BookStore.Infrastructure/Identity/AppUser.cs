using Microsoft.AspNetCore.Identity;

namespace BookStore.Infrastructure.Identity;

/// <summary>
/// Account managed by ASP.NET Core Identity. Lives in Infrastructure so the domain stays
/// framework-free; domain entities reference users by <c>UserId</c> only.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public required string FullName { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
