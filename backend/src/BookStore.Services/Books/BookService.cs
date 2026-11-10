using BookStore.Domain.Common;
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

}

internal sealed class BookService(
    IBookRepository books,
    IAuthorRepository authors,
    IGenreRepository genres,
    IPublisherRepository publishers,
    ISeriesRepository series,
    IRatingRepository ratings,
    IUnitOfWork unitOfWork) : IBookService
{
    public async Task<PagedResult<BookDto>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? genreIds = null;
        if (query.GenreId is { } genreId)
        {
            genreIds = GenreTree.WithDescendants(await genres.GetAllAsync(cancellationToken), genreId);
        }

        var filter = new BookFilter(
            Search: string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            GenreIds: genreIds,
            AuthorId: query.AuthorId,
            PublisherId: query.PublisherId,
            SeriesId: query.SeriesId,
            Language: string.IsNullOrWhiteSpace(query.Language) ? null : query.Language.Trim().ToLowerInvariant(),
            MinPrice: query.MinPrice,
            MaxPrice: query.MaxPrice,
            MinYear: query.MinYear,
            MaxYear: query.MaxYear,
            InStockOnly: query.InStockOnly,
            Sort: query.Sort,
            Page: query.Page,
            PageSize: query.PageSize);
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

        if (await books.HasOrdersAsync(id, cancellationToken))
        {
            throw new ConflictException(
                "The book has orders and can't be deleted. Set its stock to 0 to take it off sale.");
        }

        books.Remove(book);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(Book book, UpsertBookRequest request, CancellationToken cancellationToken)
    {
        var authorIds = request.AuthorIds.Distinct().ToList();
        var foundAuthors = await authors.GetByIdsAsync(authorIds, cancellationToken);
        EnsureAllFound("author", authorIds, foundAuthors.Select(a => a.Id));

        var genreIds = request.GenreIds.Distinct().ToList();
        var foundGenres = await genres.GetByIdsAsync(genreIds, cancellationToken);
        EnsureAllFound("genre", genreIds, foundGenres.Select(g => g.Id));

        if (request.PublisherId is { } publisherId && !await publishers.ExistsAsync(publisherId, cancellationToken))
        {
            throw new ValidationException($"Publisher '{publisherId}' does not exist.");
        }

        if (request.SeriesId is { } seriesId && !await series.ExistsAsync(seriesId, cancellationToken))
        {
            throw new ValidationException($"Series '{seriesId}' does not exist.");
        }

        book.Title = request.Title.Trim();
        book.Isbn = request.Isbn;
        book.Annotation = request.Annotation;
        book.Language = request.Language;
        book.Year = request.Year;
        book.Pages = request.Pages;
        book.CoverImageUrl = request.CoverImageUrl;
        book.Price = request.Price;
        book.Stock = request.Stock;
        book.PublisherId = request.PublisherId;
        book.SeriesId = request.SeriesId;

        book.Authors.Clear();
        foreach (var author in foundAuthors)
        {
            book.Authors.Add(author);
        }

        book.Genres.Clear();
        foreach (var genre in foundGenres)
        {
            book.Genres.Add(genre);
        }
    }

    private static void EnsureAllFound(string entityName, IEnumerable<Guid> requested, IEnumerable<Guid> found)
    {
        var missing = requested.Except(found).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException($"Unknown {entityName} ids: {string.Join(", ", missing)}.");
        }
    }
}
