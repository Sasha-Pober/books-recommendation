using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Persistence;

/// <summary>Applies migrations and seeds a small demo catalog. Intended for development.</summary>
public sealed class DatabaseInitializer(BookStoreDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Books.AnyAsync(cancellationToken))
        {
            return;
        }

        logger.LogInformation("Seeding demo catalog");
        SeedCatalog();
        await db.SaveChangesAsync(cancellationToken);
    }

    private void SeedCatalog()
    {
        var genres = new[] { "Fantasy", "Science Fiction", "Classics", "Dystopia", "Mystery", "Adventure" }
            .ToDictionary(name => name, name => new Genre { Name = name });

        var authors = new[]
            {
                "J. R. R. Tolkien", "Frank Herbert", "George Orwell", "Isaac Asimov",
                "Arthur Conan Doyle", "Jane Austen", "Ursula K. Le Guin", "Agatha Christie",
            }
            .ToDictionary(name => name, name => new Author { FullName = name });

        Book NewBook(string title, string author, int year, decimal price, params string[] genreNames) => new()
        {
            Title = title,
            Author = authors[author],
            PublicationYear = year,
            Price = price,
            Stock = 10,
            Language = "en",
            Genres = genreNames.Select(g => genres[g]).ToList(),
        };

        db.Books.AddRange(
            NewBook("The Hobbit", "J. R. R. Tolkien", 1937, 12.99m, "Fantasy", "Adventure"),
            NewBook("The Fellowship of the Ring", "J. R. R. Tolkien", 1954, 15.99m, "Fantasy", "Adventure"),
            NewBook("Dune", "Frank Herbert", 1965, 14.50m, "Science Fiction", "Adventure"),
            NewBook("Dune Messiah", "Frank Herbert", 1969, 13.50m, "Science Fiction"),
            NewBook("Nineteen Eighty-Four", "George Orwell", 1949, 9.99m, "Dystopia", "Classics"),
            NewBook("Animal Farm", "George Orwell", 1945, 7.99m, "Classics"),
            NewBook("Foundation", "Isaac Asimov", 1951, 11.99m, "Science Fiction"),
            NewBook("I, Robot", "Isaac Asimov", 1950, 10.99m, "Science Fiction"),
            NewBook("A Study in Scarlet", "Arthur Conan Doyle", 1887, 6.99m, "Mystery", "Classics"),
            NewBook("The Hound of the Baskervilles", "Arthur Conan Doyle", 1902, 7.49m, "Mystery", "Classics"),
            NewBook("Pride and Prejudice", "Jane Austen", 1813, 8.99m, "Classics"),
            NewBook("A Wizard of Earthsea", "Ursula K. Le Guin", 1968, 11.49m, "Fantasy"),
            NewBook("The Left Hand of Darkness", "Ursula K. Le Guin", 1969, 12.49m, "Science Fiction"),
            NewBook("Murder on the Orient Express", "Agatha Christie", 1934, 9.49m, "Mystery"));
    }
}
