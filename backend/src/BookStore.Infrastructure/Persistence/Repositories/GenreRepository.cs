using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class GenreRepository(BookStoreDbContext db) : IGenreRepository
{
    public async Task<IReadOnlyList<Genre>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Genres.AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Genre>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Genres.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken) =>
        db.Genres.AnyAsync(g => g.Name == name, cancellationToken);

    public void Add(Genre genre) => db.Genres.Add(genre);
}
