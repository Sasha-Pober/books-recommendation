using BookStore.Domain.Entities;

namespace BookStore.Domain.Services;

/// <summary>
/// Buffer for high-volume events (page views): the request returns at once and a background writer
/// saves the events in batches. Events tied to a business action (cart, purchase, rating) are saved
/// together with that action instead.
/// </summary>
public interface IInteractionQueue
{
    /// <summary>Queues all events, or none if the buffer has no room for them.</summary>
    bool TryEnqueue(IReadOnlyCollection<Interaction> interactions);
}
