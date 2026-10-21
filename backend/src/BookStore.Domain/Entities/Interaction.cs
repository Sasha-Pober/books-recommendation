using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Entities;

/// <summary>
/// A single user event (view, purchase, ...). Append-only log that the Python
/// training pipeline reads to build the user–item matrix.
/// Uses a database-generated <c>long</c> key: this is by far the largest table,
/// it is never addressed by id through the API, and the sequential key keeps it compact.
/// </summary>
public class Interaction : Entity<long>
{
    private Interaction()
    {
    }

    /// <summary>Null for guests; <see cref="SessionId"/> identifies them instead.</summary>
    public Guid? UserId { get; private set; }

    public string SessionId { get; private set; } = null!;

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public InteractionType EventType { get; private set; }

    /// <summary>Strength of interest, see <see cref="InteractionWeights"/>.</summary>
    public double Weight { get; private set; }

    /// <param name="ratingScore">Required for <see cref="InteractionType.Rating"/>, ignored otherwise.</param>
    public static Interaction Create(
        Guid? userId, string sessionId, Guid bookId, InteractionType eventType, int? ratingScore = null) => new()
    {
        UserId = userId,
        SessionId = sessionId,
        BookId = bookId,
        EventType = eventType,
        Weight = InteractionWeights.For(eventType, ratingScore),
    };
}
