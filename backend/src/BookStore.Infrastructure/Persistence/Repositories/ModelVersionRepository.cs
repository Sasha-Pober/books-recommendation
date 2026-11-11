using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class ModelVersionRepository(BookStoreDbContext db) : IModelVersionRepository
{
    public async Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.ModelVersions
            .AsNoTracking()
            .OrderByDescending(m => m.TrainedAt)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken);
}
