using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class PublisherRepository(BookStoreDbContext db) : IPublisherRepository
{
    public async Task<IReadOnlyList<Publisher>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Publishers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Publishers.AnyAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken) =>
        db.Publishers.AnyAsync(x => x.Name == name, cancellationToken);

    public void Add(Publisher entity) => db.Publishers.Add(entity);
}
