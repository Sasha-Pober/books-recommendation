using BookStore.Domain.Entities;
using BookStore.Domain.Services;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Events;

/// <summary>Writes one batch of queued views and marks clicked recommendations. Scoped: a fresh DbContext per batch.</summary>
internal sealed class InteractionBatchStore(BookStoreDbContext db, ILogger<InteractionBatchStore> logger)
{
    /// <returns>Number of events saved.</returns>
    public async Task<int> SaveAsync(IReadOnlyList<QueuedView> batch, CancellationToken cancellationToken)
    {
        var valid = await DropUnknownReferencesAsync(batch, cancellationToken);
        if (valid.Count == 0)
        {
            return 0;
        }

        try
        {
            db.Interactions.AddRange(valid.Select(v => v.Interaction));
            await MarkClickedAsync(valid, cancellationToken);
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

    /// <summary>
    /// Marks recommendation logs as clicked (tracked, saved with the events). A log only counts if it was
    /// shown to the same user (or to a guest, for guest views) and for the same book, so a client can't
    /// mark arbitrary log ids.
    /// </summary>
    private async Task MarkClickedAsync(IReadOnlyList<QueuedView> views, CancellationToken cancellationToken)
    {
        var clicks = views.Where(v => v.RecommendationLogId is not null).ToList();
        if (clicks.Count == 0)
        {
            return;
        }

        var logIds = clicks.Select(v => v.RecommendationLogId!.Value).Distinct().ToList();
        var logs = await db.RecommendationLogs
            .Where(l => logIds.Contains(l.Id) && !l.Clicked)
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        foreach (var click in clicks)
        {
            if (logs.TryGetValue(click.RecommendationLogId!.Value, out var log)
                && log.BookId == click.Interaction.BookId
                && log.UserId == click.Interaction.UserId)
            {
                log.Clicked = true;
            }
        }
    }

    /// <summary>Events for books that don't exist, or from accounts that were deleted, can't be saved.</summary>
    private async Task<List<QueuedView>> DropUnknownReferencesAsync(
        IReadOnlyList<QueuedView> batch, CancellationToken cancellationToken)
    {
        var bookIds = batch.Select(v => v.Interaction.BookId).Distinct().ToList();
        var knownBooks = (await db.Books
            .Where(b => bookIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var userIds = batch
            .Where(v => v.Interaction.UserId is not null)
            .Select(v => v.Interaction.UserId!.Value)
            .Distinct()
            .ToList();
        var knownUsers = userIds.Count == 0
            ? []
            : (await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
            .ToHashSet();

        var valid = batch
            .Where(v => knownBooks.Contains(v.Interaction.BookId)
                        && (v.Interaction.UserId is not { } userId || knownUsers.Contains(userId)))
            .ToList();
        if (valid.Count < batch.Count)
        {
            logger.LogInformation(
                "Dropped {Count} events referencing unknown books or users", batch.Count - valid.Count);
        }

        return valid;
    }

    private async Task<int> SaveIndividuallyAsync(List<QueuedView> views, CancellationToken cancellationToken)
    {
        var saved = 0;
        foreach (var view in views)
        {
            try
            {
                db.Interactions.Add(view.Interaction);
                await MarkClickedAsync([view], cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                saved++;
            }
            catch (DbUpdateException ex) when (!IsTransient(ex))
            {
                logger.LogWarning(ex, "Dropped event for book {BookId}", view.Interaction.BookId);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return saved;
    }
}
