using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;

namespace BookStore.IntegrationTests;

internal static class TestData
{
    public static Book Book(
        string title,
        Author author,
        int stock = 5,
        decimal price = 100,
        int? year = 2000,
        string language = "uk",
        string? annotation = null,
        params Genre[] genres) => new()
    {
        Title = title,
        Annotation = annotation,
        Authors = [author],
        Stock = stock,
        Price = price,
        Year = year,
        Language = language,
        Genres = genres.ToList(),
    };

    public static IEnumerable<Interaction> Interactions(Book book, InteractionType type, int times, Guid? userId = null) =>
        Enumerable.Range(0, times).Select(_ => Interaction.Create(userId, "session", book.Id, type));

    /// <summary>A user row (foreign keys to users are enforced in PostgreSQL).</summary>
    public static async Task<AppUser> AddUserAsync(BookStoreDbContext db, string email = "reader@example.com")
    {
        var user = new AppUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Reader",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
