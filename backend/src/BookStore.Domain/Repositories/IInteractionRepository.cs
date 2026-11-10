using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;

namespace BookStore.Domain.Repositories;

public interface IInteractionRepository
{
    Task<IReadOnlyList<Guid>> GetPurchasedBookIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>In-stock books ranked by the sum of their interaction weights since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<ScoredBook>> GetPopularBooksAsync(
        DateTime since, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken);

    void AddRange(IEnumerable<Interaction> interactions);

    /// <summary>Attaches the guest events of a browser session to the user who has just signed in. Runs immediately.</summary>
    Task<int> AssignGuestSessionAsync(string sessionId, Guid userId, CancellationToken cancellationToken);
}
