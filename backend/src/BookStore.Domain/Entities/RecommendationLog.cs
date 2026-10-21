using BookStore.Domain.Enums;

namespace BookStore.Domain.Entities;

/// <summary>One recommended book shown to a user. Used to measure CTR and run A/B tests.</summary>
public class RecommendationLog
{
    /// <summary>Database-generated; high-volume table like interactions.</summary>
    public long Id { get; set; }

    /// <summary>Null for guests.</summary>
    public Guid? UserId { get; set; }

    public Guid BookId { get; set; }

    public RecommendationPlacement Placement { get; set; }

    /// <summary>0-based position in the shown list.</summary>
    public int Position { get; set; }

    /// <summary>Null when the list came from the non-personalized fallback.</summary>
    public int? ModelVersionId { get; set; }

    public bool Clicked { get; set; }

    public DateTime ShownAt { get; set; } = DateTime.UtcNow;
}
