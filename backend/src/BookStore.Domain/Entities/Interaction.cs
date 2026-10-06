using BookStore.Domain.Common;
using BookStore.Domain.Enums;

namespace BookStore.Domain.Entities;

/// <summary>
/// A single user event (view, click, purchase, ...). Append-only log
/// that the Python training pipeline reads to build the user–item matrix.
/// Uses a database-generated <c>long</c> key: this is by far the largest table,
/// it is never addressed by id through the API, and the sequential key keeps it compact.
/// </summary>
public class Interaction : Entity<long>
{
    /// <summary>Null for anonymous visitors; <see cref="SessionId"/> is used instead.</summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public required string SessionId { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public InteractionType Type { get; set; }

    /// <summary>
    /// Id of the recommendation list the user came from, if any.
    /// Lets us measure CTR per model version.
    /// </summary>
    public Guid? RecommendationId { get; set; }
}
