using BookStore.Domain.Entities;

namespace BookStore.Domain.Repositories;

public interface IModelVersionRepository
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Read-only.</summary>
    Task<ModelVersion?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
