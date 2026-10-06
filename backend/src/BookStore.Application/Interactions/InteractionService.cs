using BookStore.Application.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Interactions;

public interface IInteractionService
{
    Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken);
}

internal sealed class InteractionService(IApplicationDbContext db) : IInteractionService
{
    public async Task RecordAsync(RecordInteractionsRequest request, CancellationToken cancellationToken)
    {
        var bookIds = request.Events.Select(e => e.BookId).Distinct().ToList();
        var knownBooks = await db.Books
            .Where(b => bookIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        var unknownBooks = bookIds.Except(knownBooks).ToList();
        if (unknownBooks.Count > 0)
        {
            throw new ValidationException($"Unknown book ids: {string.Join(", ", unknownBooks)}.");
        }

        var userIds = request.Events.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct().ToList();
        var knownUsers = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        var unknownUsers = userIds.Except(knownUsers).ToList();
        if (unknownUsers.Count > 0)
        {
            throw new ValidationException($"Unknown user ids: {string.Join(", ", unknownUsers)}.");
        }

        db.Interactions.AddRange(request.Events.Select(e => new Interaction
        {
            UserId = e.UserId,
            SessionId = e.SessionId,
            BookId = e.BookId,
            Type = e.Type,
            RecommendationId = e.RecommendationId,
        }));

        await db.SaveChangesAsync(cancellationToken);

        // Next step: publish the events to the message broker so the ML service
        // can update the user's real-time features.
    }
}
