using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Books;
using BookStore.Presentation.Genres;

namespace BookStore.Services.Books;

internal static class BookMappings
{
    /// <remarks>Expects <see cref="Book.Author"/> and <see cref="Book.Genres"/> to be loaded.</remarks>
    public static BookDto ToDto(this Book book, RatingSummary rating) => new(
        book.Id,
        book.Title,
        book.Isbn,
        book.Description,
        book.PublicationYear,
        book.Language,
        book.CoverImageUrl,
        book.Price,
        book.Stock,
        new AuthorDto(book.Author.Id, book.Author.FullName, book.Author.Bio),
        book.Genres.OrderBy(g => g.Name).Select(g => new GenreDto(g.Id, g.Name)).ToList(),
        rating.Average,
        rating.Count);

    public static RatingSummary For(this IReadOnlyDictionary<Guid, RatingSummary> summaries, Guid bookId) =>
        summaries.GetValueOrDefault(bookId, RatingSummary.Empty);
}
