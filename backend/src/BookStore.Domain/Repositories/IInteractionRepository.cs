using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Repositories;

public interface IInteractionRepository
{
    Task<IReadOnlyList<Guid>> GetPurchasedBookIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>In-stock books ranked by the weighted sum of their interactions since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<ScoredBook>> GetPopularBooksAsync(
        DateTime since, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    void AddRange(IEnumerable<Interaction> interactions);
}
