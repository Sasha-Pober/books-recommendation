using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Books;
using BookStore.Presentation.Catalog;
using BookStore.Presentation.Genres;

namespace BookStore.Services.Books;

internal static class BookMappings
{
    /// <remarks>Expects authors, genres, publisher and series to be loaded.</remarks>
    public static BookDto ToDto(this Book book, RatingSummary rating) => new(
        book.Id,
        book.Title,
        book.Isbn,
        book.Annotation,
        book.Language,
        book.Year,
        book.Pages,
        book.CoverImageUrl,
        book.Price,
        book.Stock,
        book.Authors.OrderBy(a => a.FullName).Select(a => new AuthorDto(a.Id, a.FullName, a.Bio)).ToList(),
        book.Genres.OrderBy(g => g.Name).Select(g => new GenreDto(g.Id, g.Name, g.ParentId)).ToList(),
        book.Publisher is { } p ? new PublisherDto(p.Id, p.Name) : null,
        book.Series is { } s ? new SeriesDto(s.Id, s.Name) : null,
        rating.Average,
        rating.Count);

    /// <remarks>Expects authors to be loaded.</remarks>
    public static BookSummaryDto ToSummaryDto(this Book book) => new(
        book.Id,
        book.Title,
        book.Authors.OrderBy(a => a.FullName).Select(a => a.FullName).ToList(),
        book.CoverImageUrl,
        book.Price,
        book.Stock);

    public static RatingSummary For(this IReadOnlyDictionary<Guid, RatingSummary> summaries, Guid bookId) =>
        summaries.GetValueOrDefault(bookId, RatingSummary.Empty);
}
