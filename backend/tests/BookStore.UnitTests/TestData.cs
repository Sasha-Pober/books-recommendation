using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookStore.UnitTests;

internal static class TestData
{
    public static BookStoreDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BookStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static Book Book(string title, Author author, int stock = 5, params Genre[] genres) => new()
    {
        Title = title,
        Author = author,
        Stock = stock,
        Genres = genres.ToList(),
    };

    public static IEnumerable<Interaction> Interactions(Book book, InteractionType type, int times, User? user = null) =>
        Enumerable.Range(0, times).Select(_ => new Interaction
        {
            Book = book,
            User = user,
            SessionId = "session",
            Type = type,
        });
}
