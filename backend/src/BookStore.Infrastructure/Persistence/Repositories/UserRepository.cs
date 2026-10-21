using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(BookStoreDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == id, cancellationToken);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}
