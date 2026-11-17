"""Store event types and interest weights. Must match BookStore.Domain.Common.InteractionWeights (C#)."""

VIEW = "View"
ADD_TO_WISHLIST = "AddToWishlist"
ADD_TO_CART = "AddToCart"
PURCHASE = "Purchase"
RATING = "Rating"

WEIGHTS = {VIEW: 0.2, ADD_TO_WISHLIST: 0.5, ADD_TO_CART: 0.7, PURCHASE: 1.0}

# Ratings below this are not a positive signal (weight 0); 4-5 stars weigh like a purchase.
POSITIVE_RATING_THRESHOLD = 4


def rating_weight(score: int) -> float:
    return 1.0 if score >= POSITIVE_RATING_THRESHOLD else 0.0
