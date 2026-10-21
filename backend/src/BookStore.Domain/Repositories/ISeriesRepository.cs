namespace BookStore.Domain.Repositories;

public interface ISeriesRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
}
