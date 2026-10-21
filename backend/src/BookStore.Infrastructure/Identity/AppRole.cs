using Microsoft.AspNetCore.Identity;

namespace BookStore.Infrastructure.Identity;

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();
}
