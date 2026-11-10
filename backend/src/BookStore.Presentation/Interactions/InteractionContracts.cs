using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Enums;

namespace BookStore.Presentation.Interactions;

/// <remarks>The user and session are taken from the request (JWT and session cookie), not from the body.</remarks>
public sealed record InteractionEvent
{
    public required Guid BookId { get; init; }

    /// <summary><see cref="InteractionType.Rating"/> is not accepted here; ratings have their own endpoint.</summary>
    [EnumDataType(typeof(InteractionType))]
    public required InteractionType Type { get; init; }
}

/// <summary>The frontend batches events and sends them together.</summary>
public sealed record RecordInteractionsRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<InteractionEvent> Events { get; init; }
}
