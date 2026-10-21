using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class SeriesRepository(BookStoreDbContext db) : ISeriesRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Series.AnyAsync(x => x.Id == id, cancellationToken);
}
