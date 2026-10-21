using BookStore.Domain.Exceptions;
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

    private BookService CreateService() => new(
        _books,
        _authors,
        Substitute.For<IGenreRepository>(),
        Substitute.For<IUserRepository>(),
        Substitute.For<IRatingRepository>(),
        _unitOfWork);

    [Fact]
    public async Task Create_WithUnknownAuthor_ThrowsValidationException_AndSavesNothing()
    {
        _authors.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var request = new UpsertBookRequest { Title = "Title", AuthorId = Guid.NewGuid() };

        await Assert.ThrowsAsync<ValidationException>(() => CreateService().CreateAsync(request, CancellationToken.None));

        _books.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
