using BookStore.Domain.Common;
using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IAuthorRepository
{
    Task<PagedList<Author>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Author?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    void Add(Author author);
}
