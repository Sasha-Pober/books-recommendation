namespace BookStore.Domain.Enums;

public enum BookSort
{
    /// <summary>Full-text search rank; falls back to title when there is no search query.</summary>
    Relevance = 1,
    Title = 2,
    PriceAscending = 3,
    PriceDescending = 4,
    /// <summary>Most recently added to the store.</summary>
    Newest = 5,
    /// <summary>Most recent publication year.</summary>
    Year = 6,
    /// <summary>Highest average rating; unrated books last.</summary>
    Rating = 7,
}
