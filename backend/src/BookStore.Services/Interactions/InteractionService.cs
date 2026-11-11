using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Services;
using BookStore.Presentation.Interactions;

namespace BookStore.Services.Interactions;

public interface IInteractionService
{
    /// <summary>
    /// Queues events of the current user (or guest session) for a background batch write.
    /// Unknown books are dropped by the writer, so the request needs no database round trip.
    /// </summary>
    void Record(RecordInteractionsRequest request);
}

internal sealed class InteractionService(IInteractionQueue queue, ICurrentUser currentUser) : IInteractionService
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    public void Record(RecordInteractionsRequest request)
    {
        // Cart, wishlist, purchase and rating events are recorded by the server when those actions happen,
        // so clients can't inflate them and they aren't counted twice.
        if (request.Events.Any(e => e.Type != InteractionType.View))
        {
            throw new ValidationException("Only View events are accepted; other events are recorded by the server.");
        }

        var userId = currentUser.UserId;
        var sessionId = currentUser.SessionId;
        var views = request.Events
            .Select(e => new QueuedView(Interaction.Create(userId, sessionId, e.BookId, e.Type), e.RecommendationLogId))
            .ToList();

        if (!queue.TryEnqueue(views))
        {
            throw new ServiceUnavailableException("Too many events right now; retry later.", RetryAfter);
        }
    }
}
