namespace BookStore.Domain.Repositories;

/// <summary>Commits changes tracked by all repositories in the current scope as one transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> in a database transaction: committed if it completes, rolled back if it throws.
    /// Needed when the action mixes immediate updates (e.g. stock reservation) with tracked changes.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}
