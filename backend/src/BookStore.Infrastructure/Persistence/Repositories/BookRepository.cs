using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

internal sealed class BookRepository(BookStoreDbContext db) : IBookRepository
{
    private IQueryable<Book> ReadOnlyWithDetails =>
        db.Books
            .AsNoTracking()
            .Include(b => b.Authors)
            .Include(b => b.Genres)
            .Include(b => b.Publisher)
            .Include(b => b.Series)
            .AsSplitQuery();

    public Task<PagedList<Book>> SearchAsync(BookFilter filter, CancellationToken cancellationToken)
    {
        var books = ReadOnlyWithDetails;

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.ToLower();
            books = books.Where(b =>
                b.Title.ToLower().Contains(term) || b.Authors.Any(a => a.FullName.ToLower().Contains(term)));
        }

        if (filter.GenreId is { } genreId)
        {
            books = books.Where(b => b.Genres.Any(g => g.Id == genreId));
        }

        if (filter.AuthorId is { } authorId)
        {
            books = books.Where(b => b.Authors.Any(a => a.Id == authorId));
        }

        return books
            .OrderBy(b => b.Title)
            .ThenBy(b => b.Id)
            .ToPagedListAsync(filter.Page, filter.PageSize, cancellationToken);
    }

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
