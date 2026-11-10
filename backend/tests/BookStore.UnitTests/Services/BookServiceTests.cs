using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Books;
using BookStore.Services.Books;
using NSubstitute;

namespace BookStore.UnitTests.Services;

public class BookServiceTests
{
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly IAuthorRepository _authors = Substitute.For<IAuthorRepository>();
    private readonly IGenreRepository _genres = Substitute.For<IGenreRepository>();
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private BookService CreateService() => new(
        _books,
        _authors,
        _genres,
        Substitute.For<IPublisherRepository>(),
        Substitute.For<ISeriesRepository>(),
        _ratings,
        _unitOfWork);

    [Fact]
    public async Task Create_WithUnknownAuthor_ThrowsValidationException_AndSavesNothing()
    {
        _authors.GetByIdsAsync(default!, default).ReturnsForAnyArgs([]);
        var request = new UpsertBookRequest { Title = "Title", AuthorIds = [Guid.NewGuid()] };

        await Assert.ThrowsAsync<ValidationException>(() => CreateService().CreateAsync(request, CancellationToken.None));

        _books.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Search_ExpandsGenreToSubgenres_AndNormalizesInput()
    {
        var fantastic = new Genre { Name = "Фантастика" };
        var fantasy = new Genre { Name = "Фентезі", ParentId = fantastic.Id };
        _genres.GetAllAsync(default).ReturnsForAnyArgs([fantastic, fantasy]);
        _books.SearchAsync(default!, default).ReturnsForAnyArgs(new PagedList<Book>([], 1, 20, 0));
        _ratings.GetSummariesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, RatingSummary>());

        await CreateService().SearchAsync(
            new BookQuery { GenreId = fantastic.Id, Search = "  дюна ", Language = " UK " }, CancellationToken.None);

        await _books.Received(1).SearchAsync(
            Arg.Is<BookFilter>(f =>
                f.GenreIds!.Count == 2 && f.GenreIds.Contains(fantasy.Id)
                && f.Search == "дюна"
                && f.Language == "uk"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_WithBlankSearch_SendsNoSearch()
    {
        _books.SearchAsync(default!, default).ReturnsForAnyArgs(new PagedList<Book>([], 1, 20, 0));
        _ratings.GetSummariesAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, RatingSummary>());

        await CreateService().SearchAsync(new BookQuery { Search = "   " }, CancellationToken.None);

        await _books.Received(1).SearchAsync(
            Arg.Is<BookFilter>(f => f.Search == null && f.GenreIds == null), Arg.Any<CancellationToken>());
        await _genres.DidNotReceiveWithAnyArgs().GetAllAsync(default);
    }

    [Fact]
    public async Task Delete_BookWithOrders_ThrowsConflict_AndKeepsTheBook()
    {
        var book = new Book { Title = "Ordered" };
        _books.GetForUpdateAsync(book.Id, Arg.Any<CancellationToken>()).Returns(book);
        _books.HasOrdersAsync(book.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => CreateService().DeleteAsync(book.Id, CancellationToken.None));

        _books.DidNotReceiveWithAnyArgs().Remove(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
