using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class InteractionRepository(BookStoreDbContext db) : IInteractionRepository
{
    public async Task<IReadOnlyList<Guid>> GetPurchasedBookIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Interactions
            .Where(i => i.UserId == userId && i.EventType == InteractionType.Purchase)
            .Select(i => i.BookId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScoredBook>> GetPopularBooksAsync(
        DateTime since, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken)
    {
        var popular = await db.Interactions
            .Where(i => i.CreatedAt >= since && i.Book.Stock > 0 && !excludeIds.Contains(i.BookId))
            .GroupBy(i => i.BookId)
            .Select(g => new
            {
                BookId = g.Key,
                Score = g.Sum(i => i.Weight),
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToListAsync(cancellationToken);

        return popular.Select(x => new ScoredBook(x.BookId, x.Score)).ToList();
    }

    public void AddRange(IEnumerable<Interaction> interactions) => db.Interactions.AddRange(interactions);

    public Task<int> AssignGuestSessionAsync(string sessionId, Guid userId, CancellationToken cancellationToken) =>
        db.Interactions
            .Where(i => i.SessionId == sessionId && i.UserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UserId, (Guid?)userId), cancellationToken);
}
