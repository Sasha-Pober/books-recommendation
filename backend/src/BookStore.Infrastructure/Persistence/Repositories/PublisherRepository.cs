using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class PublisherRepository(BookStoreDbContext db) : IPublisherRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Publishers.AnyAsync(x => x.Id == id, cancellationToken);
}
