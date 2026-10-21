using System.Linq.Expressions;
using BookStore.Application.Authors;
using BookStore.Application.Genres;
using BookStore.Domain.Entities;

namespace BookStore.Application.Books;

internal static class BookMappings
{
    /// <summary>EF-translatable projection, so only needed columns are queried.</summary>
    public static readonly Expression<Func<Book, BookDto>> ToDto = b => new BookDto(
        b.Id,
        b.Title,
        b.Isbn,
        b.Description,
        b.PublicationYear,
        b.Language,
        b.CoverImageUrl,
        b.Price,
        b.Stock,
        new AuthorDto(b.Author.Id, b.Author.FullName, b.Author.Bio),
        b.Genres.OrderBy(g => g.Name).Select(g => new GenreDto(g.Id, g.Name)).ToList(),
        b.Ratings.Average(r => (double?)r.Score),
        b.Ratings.Count);
}
