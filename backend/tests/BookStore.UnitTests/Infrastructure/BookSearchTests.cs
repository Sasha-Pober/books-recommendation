using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Repositories;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.UnitTests.Infrastructure;

public class BookSearchTests
{
    private static BookFilter Filter(
        string? search = null,
        BookSort? sort = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int? minYear = null,
        bool inStockOnly = false,
        string? language = null,
        IReadOnlyCollection<Guid>? genreIds = null) =>
        new(search, genreIds, null, null, null, language, minPrice, maxPrice, minYear, null, inStockOnly, sort, 1, 20);

    // Filters and sorting without full-text search run on the InMemory provider.

    [Fact]
    public async Task Filters_ByPriceYearStockLanguageAndGenre()
    {
        await using var db = TestData.CreateDbContext();
        var author = new Author { FullName = "Author" };
        var fantasy = new Genre { Name = "Фентезі" };
        var match = Book("Match", author, price: 200, year: 2000, genre: fantasy);
        db.Books.AddRange(
            match,
            Book("Too cheap", author, price: 50, year: 2000, genre: fantasy),
            Book("Too old", author, price: 200, year: 1900, genre: fantasy),
            Book("Out of stock", author, price: 200, year: 2000, stock: 0, genre: fantasy),
            Book("English", author, price: 200, year: 2000, language: "en", genre: fantasy),
            Book("Other genre", author, price: 200, year: 2000));
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).SearchAsync(
            Filter(minPrice: 100, maxPrice: 300, minYear: 1950, inStockOnly: true, language: "uk", genreIds: [fantasy.Id]),
            CancellationToken.None);

        Assert.Equal([match.Id], result.Items.Select(b => b.Id));
        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData(BookSort.PriceAscending, new[] { "B", "C", "A" })]
    [InlineData(BookSort.PriceDescending, new[] { "A", "C", "B" })]
    [InlineData(BookSort.Year, new[] { "C", "A", "B" })]
    [InlineData(BookSort.Title, new[] { "A", "B", "C" })]
    [InlineData(null, new[] { "A", "B", "C" })]
    public async Task Sorts(BookSort? sort, string[] expectedTitles)
    {
        await using var db = TestData.CreateDbContext();
        var author = new Author { FullName = "Author" };
        db.Books.AddRange(
            Book("A", author, price: 300, year: 1990),
            Book("B", author, price: 100, year: null),
            Book("C", author, price: 200, year: 2020));
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).SearchAsync(Filter(sort: sort), CancellationToken.None);

        Assert.Equal(expectedTitles, result.Items.Select(b => b.Title));
    }

    // Full-text search is PostgreSQL-only: check the generated SQL (no database connection needed).

    [Fact]
    public void Search_UsesUkrainianFullTextSearch_RankedByRelevance()
    {
        using var db = CreateNpgsqlContext();

        var sql = new BookRepository(db).BuildSearchQuery(Filter(search: "дюна")).ToQueryString();

        Assert.Contains("b.search_vector @@ websearch_to_tsquery('ukrainian'", sql);
        Assert.Contains("ts_rank(b.search_vector, websearch_to_tsquery('ukrainian'", sql);
        Assert.Contains("ILIKE", sql);
        Assert.Contains("ESCAPE", sql);
    }

    [Fact]
    public void Search_WithExplicitSort_DoesNotRankByRelevance()
    {
        using var db = CreateNpgsqlContext();

        var sql = new BookRepository(db)
            .BuildSearchQuery(Filter(search: "дюна", sort: BookSort.PriceAscending))
            .ToQueryString();

        Assert.Contains("@@ websearch_to_tsquery", sql);
        Assert.DoesNotContain("ts_rank", sql);
    }

    [Fact]
    public void RatingSort_PutsUnratedBooksLast()
    {
        using var db = CreateNpgsqlContext();

        var sql = new BookRepository(db).BuildSearchQuery(Filter(sort: BookSort.Rating)).ToQueryString();

        // false sorts before true: rated books first, then by average rating.
        Assert.Contains("ORDER BY NOT EXISTS", sql);
        Assert.Contains("avg(r0.score::double precision)", sql);
    }

    private static BookStoreDbContext CreateNpgsqlContext() =>
        new(new DbContextOptionsBuilder<BookStoreDbContext>()
            .UseNpgsql("Host=localhost;Database=unused", npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options);

    private static Book Book(
        string title,
        Author author,
        decimal price = 100,
        int? year = 2000,
        int stock = 5,
        string language = "uk",
        Genre? genre = null) => new()
    {
        Title = title,
        Authors = [author],
        Price = price,
        Year = year,
        Stock = stock,
        Language = language,
        Genres = genre is null ? [] : [genre],
    };
}
