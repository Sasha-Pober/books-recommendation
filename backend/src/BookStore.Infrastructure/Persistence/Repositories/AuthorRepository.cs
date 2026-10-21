using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class AuthorRepository(BookStoreDbContext db) : IAuthorRepository
{
    public Task<PagedList<Author>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        db.Authors
            .AsNoTracking()
            .OrderBy(a => a.FullName)
            .ThenBy(a => a.Id)
            .ToPagedListAsync(page, pageSize, cancellationToken);

    public Task<Author?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Authors.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Author>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Authors.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);

    public void Add(Author author) => db.Authors.Add(author);
}
