using System.ComponentModel.DataAnnotations;
using BookStore.Domain.Entities;

namespace BookStore.Presentation.Ratings;

/// <remarks>The user is taken from the JWT. Rating the same book again replaces the previous rating.</remarks>
public sealed record RateBookRequest
{
    [Range(Rating.MinScore, Rating.MaxScore)]
    public int Score { get; init; }

    [MaxLength(4000)]
    public string? ReviewText { get; init; }
}

public sealed record ReviewDto(string ReviewerName, int Score, string? ReviewText, DateTime CreatedAt, DateTime UpdatedAt);
