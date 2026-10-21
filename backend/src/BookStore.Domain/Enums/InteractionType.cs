namespace BookStore.Domain.Enums;

/// <summary>User events collected as implicit (and explicit, for ratings) feedback.</summary>
public enum InteractionType
{
    View = 1,
    AddToWishlist = 2,
    AddToCart = 3,
    Purchase = 4,
    Rating = 5,
}
