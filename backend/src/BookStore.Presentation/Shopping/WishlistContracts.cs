using BookStore.Presentation.Books;

namespace BookStore.Presentation.Shopping;

public sealed record WishlistItemDto(BookSummaryDto Book, DateTime AddedAt);

public sealed record AddToWishlistRequest(Guid BookId);
