using BookStore.Application.Books;
using BookStore.Application.Common.Exceptions;

namespace BookStore.UnitTests.Books;

public class BookServiceTests
{
    [Fact]
    public async Task Create_WithUnknownAuthor_ThrowsValidationException()
    {
        await using var db = TestData.CreateDbContext();
        var service = new BookService(db);

        var request = new UpsertBookRequest { Title = "Title", AuthorId = Guid.NewGuid() };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request, CancellationToken.None));
    }
}
