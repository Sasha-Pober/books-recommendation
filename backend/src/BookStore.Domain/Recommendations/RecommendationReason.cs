namespace BookStore.Domain.Recommendations;

/// <summary>Why a book was recommended; shown to the user as a short explanation.</summary>
public enum RecommendationReason
{
    /// <summary>Content component dominates: close to books the user viewed or bought.</summary>
    SimilarToViewed = 1,

    /// <summary>Collaborative component dominates: chosen by readers with similar tastes.</summary>
    LikedBySimilarReaders = 2,

    /// <summary>A recent book by an author the user read.</summary>
    NewFromFavoriteAuthor = 3,

    /// <summary>Non-personalized: most sold recently.</summary>
    Bestseller = 4,

    /// <summary>Non-personalized: most viewed / added recently (used when there are few sales).</summary>
    Trending = 5,

    /// <summary>Non-personalized: recently added to the store.</summary>
    NewRelease = 6,

    /// <summary>"Similar books": shares an author with the current book.</summary>
    SameAuthor = 7,

    /// <summary>"Similar books": similar content or genres.</summary>
    SimilarContent = 8,
}
