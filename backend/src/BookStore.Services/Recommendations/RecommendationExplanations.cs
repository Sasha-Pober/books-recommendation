using BookStore.Domain.Recommendations;

namespace BookStore.Services.Recommendations;

/// <summary>Short Ukrainian explanations shown next to recommendations.</summary>
internal static class RecommendationExplanations
{
    public static string? For(RecommendationReason? reason) => reason switch
    {
        RecommendationReason.SimilarToViewed => "Схожа на книги, які ви переглядали",
        RecommendationReason.LikedBySimilarReaders => "Читачі зі схожими смаками обирали",
        RecommendationReason.NewFromFavoriteAuthor => "Новий твір улюбленого автора",
        RecommendationReason.Bestseller => "Бестселер",
        RecommendationReason.Trending => "Популярне зараз",
        RecommendationReason.NewRelease => "Новинка",
        RecommendationReason.SameAuthor => "Того ж автора",
        RecommendationReason.SimilarContent => "Схожа за змістом і жанром",
        _ => null,
    };
}
