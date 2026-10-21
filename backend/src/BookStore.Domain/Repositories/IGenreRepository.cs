using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IGenreRepository
{
    Task<IReadOnlyList<Genre>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Tracked, so the genres can be attached to a book.</summary>
    Task<IReadOnlyList<Genre>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Genre genre);
}
