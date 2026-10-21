namespace BookStore.Domain.Repositories;

/// <summary>Users are managed by ASP.NET Core Identity; entities reference them by id only.</summary>
public interface IUserRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
