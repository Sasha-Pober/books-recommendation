namespace BookStore.Domain.Repositories;

/// <summary>Commits changes tracked by all repositories in the current scope as one transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
