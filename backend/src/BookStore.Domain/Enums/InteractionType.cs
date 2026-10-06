namespace BookStore.Domain.Enums;

/// <summary>
/// Implicit feedback signals collected from the frontend.
/// These are the main training data for the recommendation model.
/// </summary>
public enum InteractionType
{
    View = 1,
    Click = 2,
    AddToWishlist = 3,
    AddToCart = 4,
    Purchase = 5,
}
