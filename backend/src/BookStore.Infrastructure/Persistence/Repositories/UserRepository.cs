using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(BookStoreDbContext db) : IUserRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
}
