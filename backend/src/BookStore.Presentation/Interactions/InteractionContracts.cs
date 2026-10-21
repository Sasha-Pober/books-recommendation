using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Enums;

namespace BookStore.Application.Interactions;

public sealed record InteractionEvent
{
    // TODO: take from the JWT once authentication is added.
    public Guid? UserId { get; init; }

    [Required, MaxLength(100)]
    public required string SessionId { get; init; }

    public required Guid BookId { get; init; }

    [EnumDataType(typeof(InteractionType))]
    public required InteractionType Type { get; init; }

    public Guid? RecommendationId { get; init; }
}

/// <summary>The frontend batches events and sends them together.</summary>
public sealed record RecordInteractionsRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<InteractionEvent> Events { get; init; }
}
