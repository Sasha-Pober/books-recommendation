using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Books;
using BookStore.Presentation.Common;

namespace BookStore.Services.Books;

public interface IBookService
{
    Task<PagedResult<BookDto>> SearchAsync(BookQuery query, CancellationToken cancellationToken);

    Task<BookDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<BookDto> CreateAsync(UpsertBookRequest request, CancellationToken cancellationToken);

    Task<BookDto> UpdateAsync(Guid id, UpsertBookRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken);
}

internal sealed class BookService(
    IBookRepository books,
    IAuthorRepository authors,
    IGenreRepository genres,
    IUserRepository users,
    IRatingRepository ratings,
    IUnitOfWork unitOfWork) : IBookService
{
    public async Task<PagedResult<BookDto>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
    {
        var filter = new BookFilter(query.Search?.Trim(), query.GenreId, query.AuthorId, query.Page, query.PageSize);
        var page = await books.SearchAsync(filter, cancellationToken);
        var summaries = await ratings.GetSummariesAsync(page.Items.Select(b => b.Id).ToList(), cancellationToken);

        return new PagedResult<BookDto>(
            page.Items.Select(b => b.ToDto(summaries.For(b.Id))).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<BookDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), id);
        var summaries = await ratings.GetSummariesAsync([id], cancellationToken);

        return book.ToDto(summaries.For(id));
    }

    public async Task<BookDto> CreateAsync(UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = new Book { Title = request.Title };
        await ApplyAsync(book, request, cancellationToken);

        books.Add(book);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(book.Id, cancellationToken);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var book = await books.GetForUpdateAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), id);

        await ApplyAsync(book, request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(book.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await books.GetForUpdateAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Book), id);

        books.Remove(book);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RateAsync(Guid bookId, RateBookRequest request, CancellationToken cancellationToken)
    {
        if (!await books.ExistsAsync(bookId, cancellationToken))
        {
            throw new NotFoundException(nameof(Book), bookId);
        }

        if (!await users.ExistsAsync(request.UserId, cancellationToken))
        {
            throw new NotFoundException(nameof(User), request.UserId);
        }

        var rating = await ratings.GetAsync(request.UserId, bookId, cancellationToken);
        if (rating is null)
        {
            ratings.Add(new Rating { BookId = bookId, UserId = request.UserId, Score = request.Score });
        }
        else
        {
            rating.Score = request.Score;
            rating.UpdatedAt = DateTime.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(Book book, UpsertBookRequest request, CancellationToken cancellationToken)
    {
        if (!await authors.ExistsAsync(request.AuthorId, cancellationToken))
        {
            throw new ValidationException($"Author '{request.AuthorId}' does not exist.");
        }

        var genreIds = request.GenreIds.Distinct().ToList();
        var foundGenres = await genres.GetByIdsAsync(genreIds, cancellationToken);
        if (foundGenres.Count != genreIds.Count)
        {
            var missing = genreIds.Except(foundGenres.Select(g => g.Id));
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
        foreach (var genre in foundGenres)
        {
            book.Genres.Add(genre);
        }
    }
}
