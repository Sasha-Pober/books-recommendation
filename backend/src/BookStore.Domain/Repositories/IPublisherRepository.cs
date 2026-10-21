namespace BookStore.Domain.Repositories;

public interface IPublisherRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
}
