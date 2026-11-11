using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Enums;

namespace BookStore.Presentation.Interactions;

/// <remarks>The user and session are taken from the request (JWT and session cookie), not from the body.</remarks>
public sealed record InteractionEvent
{
    public required Guid BookId { get; init; }

    /// <summary>
    /// Only <see cref="InteractionType.View"/>: cart, wishlist, purchase and rating events are recorded
    /// by the server when those actions happen.
    /// </summary>
    [EnumDataType(typeof(InteractionType))]
    public required InteractionType Type { get; init; }

    /// <summary>
    /// <c>logId</c> of the recommendation the user clicked to open this book, if any;
    /// marks that recommendation as clicked.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? RecommendationLogId { get; init; }
}

/// <summary>The frontend batches events and sends them together.</summary>
public sealed record RecordInteractionsRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<InteractionEvent> Events { get; init; }
}
