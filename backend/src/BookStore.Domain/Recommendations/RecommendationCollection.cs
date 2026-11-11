namespace BookStore.Domain.Recommendations;

/// <summary>Non-personalized collections, precomputed and cached; the fallback when the ML service is unavailable.</summary>
public enum RecommendationCollection
{
    Bestsellers = 1,
    NewReleases = 2,
}
