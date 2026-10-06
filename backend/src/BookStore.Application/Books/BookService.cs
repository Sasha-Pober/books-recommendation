using BookStore.Application.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Books;

public interface IBookService
{
    Task<PagedResult<BookDto>> SearchAsync(BookQuery query, CancellationToken cancellationToken);

    Task<BookDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<BookDto> CreateAsync(UpsertBookRequest request, CancellationToken cancellationToken);

    Task<BookDto> UpdateAsync(Guid id, UpsertBookRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken);
}

internal sealed class BookService(IApplicationDbContext db) : IBookService
{
    public Task<PagedResult<BookDto>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
    {
        var books = db.Books.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            books = books.Where(b =>
                b.Title.ToLower().Contains(term) || b.Author.FullName.ToLower().Contains(term));
        }

        if (query.GenreId is { } genreId)
        {
            books = books.Where(b => b.Genres.Any(g => g.Id == genreId));
        }

        if (query.AuthorId is { } authorId)
        {
            books = books.Where(b => b.AuthorId == authorId);
        }

        return books
            .OrderBy(b => b.Title)
            .ThenBy(b => b.Id)
            .Select(BookMappings.ToDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<BookDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Books
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(BookMappings.ToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Book), id);

    public async Task<BookDto> CreateAsync(UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = new Book { Title = request.Title };
        await ApplyAsync(book, request, cancellationToken);

        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(book.Id, cancellationToken);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = await db.Books
                       .Include(b => b.Genres)
                       .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), id);

        await ApplyAsync(book, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(book.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await db.Books.FindAsync([id], cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), id);

        db.Books.Remove(book);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
        {
            throw new NotFoundException(nameof(User), request.UserId);
        }

        var rating = await db.Ratings.FirstOrDefaultAsync(
            r => r.BookId == bookId && r.UserId == request.UserId, cancellationToken);

        if (rating is null)
        {
            db.Ratings.Add(new Rating { BookId = bookId, UserId = request.UserId, Score = request.Score });
        }
        else
        {
            rating.Score = request.Score;
            rating.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(Book book, UpsertBookRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Authors.AnyAsync(a => a.Id == request.AuthorId, cancellationToken))
        {
            throw new ValidationException($"Author '{request.AuthorId}' does not exist.");
        }

        var genreIds = request.GenreIds.Distinct().ToList();
        var genres = await db.Genres.Where(g => genreIds.Contains(g.Id)).ToListAsync(cancellationToken);
        if (genres.Count != genreIds.Count)
        {
            var missing = genreIds.Except(genres.Select(g => g.Id));
            throw new ValidationException($"Unknown genre ids: {string.Join(", ", missing)}.");
        }

        book.Title = request.Title.Trim();
        book.Isbn = request.Isbn;
        book.Description = request.Description;
        book.PublicationYear = request.PublicationYear;
        book.Language = request.Language;
        book.CoverImageUrl = request.CoverImageUrl;
        book.Price = request.Price;
        book.Stock = request.Stock;
        book.AuthorId = request.AuthorId;

        book.Genres.Clear();
        foreach (var genre in genres)
        {
            book.Genres.Add(genre);
        }
    }
}
