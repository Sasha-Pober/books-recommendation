using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Books;
using BookStore.Services.Books;
using NSubstitute;

namespace BookStore.UnitTests.Services;

public class BookServiceTests
{
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly IAuthorRepository _authors = Substitute.For<IAuthorRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private BookService CreateService() => new(
        _books,
        _authors,
        Substitute.For<IGenreRepository>(),
        Substitute.For<IPublisherRepository>(),
        Substitute.For<ISeriesRepository>(),
        Substitute.For<IUserRepository>(),
        Substitute.For<IRatingRepository>(),
        _currentUser,
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
    public async Task Rate_AsGuest_ThrowsUnauthorized()
    {
        _books.ExistsAsync(default, default).ReturnsForAnyArgs(true);
        _currentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RateAsync(Guid.NewGuid(), new RateBookRequest { Score = 5 }, CancellationToken.None));
    }
}
