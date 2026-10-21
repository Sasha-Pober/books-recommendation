using BookStore.Domain.Enums;

namespace BookStore.Domain.Common;

/// <summary>
/// Interest weights of user events. The interest of user u in book i is
/// r_ui = max of the weights of their events for that book.
/// </summary>
public static class InteractionWeights
{
    public const double View = 0.2;
    public const double Wishlist = 0.5;
    public const double Cart = 0.7;
    public const double Strong = 1.0;

    /// <summary>Ratings below this are not a positive signal.</summary>
    public const int PositiveRatingThreshold = 4;

    public static double For(InteractionType eventType, int? ratingScore = null) => eventType switch
    {
        InteractionType.View => View,
        InteractionType.AddToWishlist => Wishlist,
        InteractionType.AddToCart => Cart,
        InteractionType.Purchase => Strong,
        InteractionType.Rating => ratingScore switch
        {
            null => throw new ArgumentNullException(nameof(ratingScore), "A rating event needs a score."),
            >= PositiveRatingThreshold => Strong,
            _ => 0,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
    };
}
