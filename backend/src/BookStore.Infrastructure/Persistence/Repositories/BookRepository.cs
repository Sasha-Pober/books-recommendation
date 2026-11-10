using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class BookRepository(BookStoreDbContext db) : IBookRepository
{
    private const string LikeEscapeCharacter = "\\";

    private IQueryable<Book> ReadOnlyWithDetails =>
        db.Books
            .AsNoTracking()
            .Include(b => b.Authors)
            .Include(b => b.Genres)
            .Include(b => b.Publisher)
            .Include(b => b.Series)
            .AsSplitQuery();

    public Task<PagedList<Book>> SearchAsync(BookFilter filter, CancellationToken cancellationToken) =>
        BuildSearchQuery(filter).ToPagedListAsync(filter.Page, filter.PageSize, cancellationToken);

    /// <summary>Filtered and sorted catalog query. Separate from execution so its SQL can be tested.</summary>
    internal IQueryable<Book> BuildSearchQuery(BookFilter filter)
    {
        var books = ReadOnlyWithDetails;

        if (filter.GenreIds is { } genreIds)
        {
            books = books.Where(b => b.Genres.Any(g => genreIds.Contains(g.Id)));
        }

        if (filter.AuthorId is { } authorId)
        {
            books = books.Where(b => b.Authors.Any(a => a.Id == authorId));
        }

        if (filter.PublisherId is { } publisherId)
        {
            books = books.Where(b => b.PublisherId == publisherId);
        }

        if (filter.SeriesId is { } seriesId)
        {
            books = books.Where(b => b.SeriesId == seriesId);
        }

        if (filter.Language is { } language)
        {
            books = books.Where(b => b.Language == language);
        }

        if (filter.MinPrice is { } minPrice)
        {
            books = books.Where(b => b.Price >= minPrice);
        }

        if (filter.MaxPrice is { } maxPrice)
        {
            books = books.Where(b => b.Price <= maxPrice);
        }

        if (filter.MinYear is { } minYear)
        {
            books = books.Where(b => b.Year >= minYear);
        }

        if (filter.MaxYear is { } maxYear)
        {
            books = books.Where(b => b.Year <= maxYear);
        }

        if (filter.InStockOnly)
        {
            books = books.Where(b => b.Stock > 0);
        }

        if (filter.Search is not { } search)
        {
            return Sort(books, filter.Sort is null or BookSort.Relevance ? BookSort.Title : filter.Sort.Value);
        }

        // Full-text match uses the GIN index on search_vector; the substring match catches partial words
        // («дюн») and author names, which are not part of the vector.
        // websearch_to_tsquery must be written inline in each lambda: as a local variable EF can't translate it.
        var pattern = $"%{EscapeLikePattern(search)}%";
        books = books.Where(b =>
            EF.Property<NpgsqlTsVector>(b, BookConfiguration.SearchVector)
                .Matches(EF.Functions.WebSearchToTsQuery(BookStoreDbContext.TextSearchConfig, search))
            || EF.Functions.ILike(b.Title, pattern, LikeEscapeCharacter)
            || b.Authors.Any(a => EF.Functions.ILike(a.FullName, pattern, LikeEscapeCharacter)));

        if (filter.Sort is null or BookSort.Relevance)
        {
            // Title matches have weight A, annotation matches B; substring-only matches rank 0.
            return books
                .OrderByDescending(b => EF.Property<NpgsqlTsVector>(b, BookConfiguration.SearchVector)
                    .Rank(EF.Functions.WebSearchToTsQuery(BookStoreDbContext.TextSearchConfig, search)))
                .ThenBy(b => b.Title)
                .ThenBy(b => b.Id);
        }

        return Sort(books, filter.Sort.Value);
    }

    private IQueryable<Book> Sort(IQueryable<Book> books, BookSort sort) => sort switch
    {
        BookSort.PriceAscending => books.OrderBy(b => b.Price).ThenBy(b => b.Title).ThenBy(b => b.Id),
        BookSort.PriceDescending => books.OrderByDescending(b => b.Price).ThenBy(b => b.Title).ThenBy(b => b.Id),
        BookSort.Newest => books.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id),
        // PostgreSQL sorts NULLs first in descending order; books without a year go last.
        BookSort.Year => books.OrderBy(b => b.Year == null).ThenByDescending(b => b.Year).ThenBy(b => b.Title).ThenBy(b => b.Id),
        BookSort.Rating => books
            .OrderBy(b => !db.Ratings.Any(r => r.BookId == b.Id))
            .ThenByDescending(b => db.Ratings.Where(r => r.BookId == b.Id).Average(r => (double?)r.Score))
            .ThenBy(b => b.Title)
            .ThenBy(b => b.Id),
        _ => books.OrderBy(b => b.Title).ThenBy(b => b.Id),
    };

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadOnlyWithDetails.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<Book?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Books
            .Include(b => b.Authors)
            .Include(b => b.Genres)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Book>> GetInStockByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await ReadOnlyWithDetails
            .Where(b => ids.Contains(b.Id) && b.Stock > 0)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Books.AnyAsync(b => b.Id == id, cancellationToken);

    public Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken) =>
        db.OrderItems.AnyAsync(i => i.BookId == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Books.Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetNewestInStockIdsAsync(
        int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken) =>
        await db.Books
            .Where(b => b.Stock > 0 && !excludeIds.Contains(b.Id))
            .OrderByDescending(b => b.CreatedAt)
            .Take(count)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScoredBook>> FindSimilarByMetadataAsync(
        Guid bookId, int count, IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken)
    {
        var source = await db.Books
            .Where(b => b.Id == bookId)
            .Select(b => new
            {
                AuthorIds = b.Authors.Select(a => a.Id).ToList(),
                GenreIds = b.Genres.Select(g => g.Id).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (source is null)
        {
            return [];
        }

        var authorIds = source.AuthorIds;
        var genreIds = source.GenreIds;
        var excluded = excludeIds.Append(bookId).ToList();

        var similar = await db.Books
            .Where(b => b.Stock > 0 && !excluded.Contains(b.Id))
            .Where(b => b.Authors.Any(a => authorIds.Contains(a.Id)) || b.Genres.Any(g => genreIds.Contains(g.Id)))
            .Select(b => new
            {
                b.Id,
                Score = (b.Authors.Any(a => authorIds.Contains(a.Id)) ? 2.0 : 0.0)
                        + b.Genres.Count(g => genreIds.Contains(g.Id)),
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToListAsync(cancellationToken);

        return similar.Select(x => new ScoredBook(x.Id, x.Score)).ToList();
    }

    public void Add(Book book) => db.Books.Add(book);

    public void Remove(Book book) => db.Books.Remove(book);
}
