using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IRecommendationLogRepository
{
    /// <summary>
    /// Saves the logs immediately, independently of the unit of work, and fills in their ids.
    /// On failure nothing stays tracked, so a logging error can't break the caller's later saves.
    /// </summary>
    Task SaveAsync(IReadOnlyList<RecommendationLog> logs, CancellationToken cancellationToken);
}
