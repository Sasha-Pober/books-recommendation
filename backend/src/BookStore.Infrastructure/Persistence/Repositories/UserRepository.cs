using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(BookStoreDbContext db) : IUserRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == id, cancellationToken);

}
