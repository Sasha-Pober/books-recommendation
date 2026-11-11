using BookStore.Domain.Entities;

namespace BookStore.Domain.Services;

/// <param name="RecommendationLogId">
/// Set when the view came from a click on a recommendation; the log entry is then marked as clicked.
/// </param>
public sealed record QueuedView(Interaction Interaction, long? RecommendationLogId = null);

/// <summary>
/// Buffer for high-volume events (page views): the request returns at once and a background writer
/// saves the events in batches. Events tied to a business action (cart, purchase, rating) are saved
/// together with that action instead.
/// </summary>
public interface IInteractionQueue
{
    /// <summary>Queues all events, or none if the buffer has no room for them.</summary>
    bool TryEnqueue(IReadOnlyCollection<QueuedView> views);
}
