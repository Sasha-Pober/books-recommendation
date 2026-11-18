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

    /// <summary>C in the Bayesian average: how many "average" ratings every book starts with.</summary>
    private const double RatingPriorWeight = 10;

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

        // Three ways to match, each answered by its own index: full-text (GIN on search_vector), a part of the
        // title (trigram GIN on title, e.g. «дюн»), an author's name (trigram GIN on authors.full_name). As one
        // OR condition PostgreSQL can't use the indexes and checks every book; as a union of matching ids it can.
        // websearch_to_tsquery must be written inline in each lambda: as a local variable EF can't translate it.
        var pattern = $"%{EscapeLikePattern(search)}%";
        var matching = db.Books
            .Where(b => EF.Property<NpgsqlTsVector>(b, BookConfiguration.SearchVector)
                .Matches(EF.Functions.WebSearchToTsQuery(BookStoreDbContext.TextSearchConfig, search)))
            .Select(b => b.Id)
            .Union(db.Books.Where(b => EF.Functions.ILike(b.Title, pattern, LikeEscapeCharacter)).Select(b => b.Id))
            .Union(db.Authors
                .Where(a => EF.Functions.ILike(a.FullName, pattern, LikeEscapeCharacter))
                .SelectMany(a => a.Books.Select(b => b.Id)));
        books = books.Where(b => matching.Contains(b.Id));

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
        // Bayesian average: (sum + C·m) / (count + C), m = mean of all ratings. A book with one 5-star rating
        // doesn't outrank one with hundreds of 4.5s; it is pulled toward the overall mean until more ratings
        // arrive. Unrated books last. m is an uncorrelated subquery: PostgreSQL computes it once per query.
        BookSort.Rating => books
            .OrderBy(b => b.RatingCount == 0)
            .ThenByDescending(b =>
                (b.RatingSum + RatingPriorWeight
                    * (db.Books.Sum(x => (double)x.RatingSum) / Math.Max(db.Books.Sum(x => (double)x.RatingCount), 1.0)))
                / (b.RatingCount + RatingPriorWeight))
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

    public async Task<bool> TryReserveStockAsync(Guid bookId, int quantity, CancellationToken cancellationToken) =>
        await db.Books
            .Where(b => b.Id == bookId && b.Stock >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Stock, b => b.Stock - quantity), cancellationToken) == 1;

    public Task AdjustRatingsAsync(Guid bookId, int countDelta, int sumDelta, CancellationToken cancellationToken) =>
        db.Books
            .Where(b => b.Id == bookId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(b => b.RatingCount, b => b.RatingCount + countDelta)
                    .SetProperty(b => b.RatingSum, b => b.RatingSum + sumDelta),
                cancellationToken);

    public Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken) =>
        db.OrderItems.AnyAsync(i => i.BookId == id, cancellationToken);

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
                SameAuthor = b.Authors.Any(a => authorIds.Contains(a.Id)),
                SharedGenres = b.Genres.Count(g => genreIds.Contains(g.Id)),
            })
            .OrderByDescending(x => (x.SameAuthor ? 2 : 0) + x.SharedGenres)
            .ThenBy(x => x.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

        return similar
            .Select(x => new ScoredBook(
                x.Id,
                (x.SameAuthor ? 2.0 : 0.0) + x.SharedGenres,
                x.SameAuthor ? RecommendationReason.SameAuthor : RecommendationReason.SimilarContent))
            .ToList();
    }

    public void Add(Book book) => db.Books.Add(book);

    public void Remove(Book book) => db.Books.Remove(book);
}
