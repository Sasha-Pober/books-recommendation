using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface ISeriesRepository
{
    Task<IReadOnlyList<Series>> GetAllAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Series entity);
}
