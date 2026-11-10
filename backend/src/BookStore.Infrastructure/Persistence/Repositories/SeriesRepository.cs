using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class SeriesRepository(BookStoreDbContext db) : ISeriesRepository
{
    public async Task<IReadOnlyList<Series>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Series.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Series.AnyAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken) =>
        db.Series.AnyAsync(x => x.Name == name, cancellationToken);

    public void Add(Series entity) => db.Series.Add(entity);
}
