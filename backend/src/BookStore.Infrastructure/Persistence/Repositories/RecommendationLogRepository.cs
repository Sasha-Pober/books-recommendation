using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class RecommendationLogRepository(BookStoreDbContext db) : IRecommendationLogRepository
{
    public async Task SaveAsync(IReadOnlyList<RecommendationLog> logs, CancellationToken cancellationToken)
    {
        db.RecommendationLogs.AddRange(logs);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Ids are filled in on success; either way the logs must not be saved again by a later SaveChanges.
            foreach (var log in logs)
            {
                db.Entry(log).State = EntityState.Detached;
            }
        }
    }
}
