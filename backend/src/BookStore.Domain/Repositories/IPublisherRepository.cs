using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IPublisherRepository
{
    Task<IReadOnlyList<Publisher>> GetAllAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Publisher entity);
}
