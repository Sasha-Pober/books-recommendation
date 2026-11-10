using BookStore.Domain.Entities;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Events;

/// <summary>Writes one batch of queued events. Scoped: a fresh DbContext per batch.</summary>
internal sealed class InteractionBatchStore(BookStoreDbContext db, ILogger<InteractionBatchStore> logger)
{
    /// <returns>Number of events saved.</returns>
    public async Task<int> SaveAsync(IReadOnlyList<Interaction> batch, CancellationToken cancellationToken)
    {
        var valid = await DropUnknownReferencesAsync(batch, cancellationToken);
        if (valid.Count == 0)
        {
            return 0;
        }

        try
        {
            db.Interactions.AddRange(valid);
            await db.SaveChangesAsync(cancellationToken);
            return valid.Count;
        }
        catch (DbUpdateException ex) when (!IsTransient(ex))
        {
            // E.g. a book deleted after the check above. Save the rest one by one.
            logger.LogWarning(ex, "Batch of {Count} events failed; saving them one by one", valid.Count);
            db.ChangeTracker.Clear();
            return await SaveIndividuallyAsync(valid, cancellationToken);
        }
    }

    internal static bool IsTransient(Exception ex) =>
        ex is Npgsql.NpgsqlException { IsTransient: true }
        || (ex.InnerException is { } inner && IsTransient(inner));

    /// <summary>Events for books that don't exist, or from accounts that were deleted, can't be saved.</summary>
    private async Task<List<Interaction>> DropUnknownReferencesAsync(
        IReadOnlyList<Interaction> batch, CancellationToken cancellationToken)
    {
        var bookIds = batch.Select(i => i.BookId).Distinct().ToList();
        var knownBooks = (await db.Books
            .Where(b => bookIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var userIds = batch.Where(i => i.UserId is not null).Select(i => i.UserId!.Value).Distinct().ToList();
        var knownUsers = userIds.Count == 0
            ? []
            : (await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
            .ToHashSet();

        var valid = batch
            .Where(i => knownBooks.Contains(i.BookId) && (i.UserId is not { } userId || knownUsers.Contains(userId)))
            .ToList();
        if (valid.Count < batch.Count)
        {
            logger.LogInformation(
                "Dropped {Count} events referencing unknown books or users", batch.Count - valid.Count);
        }

        return valid;
    }

    private async Task<int> SaveIndividuallyAsync(List<Interaction> interactions, CancellationToken cancellationToken)
    {
        var saved = 0;
        foreach (var interaction in interactions)
        {
            try
            {
                db.Interactions.Add(interaction);
                await db.SaveChangesAsync(cancellationToken);
                saved++;
            }
            catch (DbUpdateException ex) when (!IsTransient(ex))
            {
                logger.LogWarning(ex, "Dropped event for book {BookId}", interaction.BookId);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return saved;
    }
}
