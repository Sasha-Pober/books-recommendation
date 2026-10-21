using BookStore.Domain.Common;
using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IAuthorRepository
{
    Task<PagedList<Author>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Author?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked, so the authors can be attached to a book.</summary>
    Task<IReadOnlyList<Author>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(Author author);
}
