using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Repositories;
using BookStore.Infrastructure.Persistence.Repositories;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>Catalog search with the Ukrainian full-text configuration, filters and sorting on PostgreSQL.</summary>
public class CatalogSearchTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Search_FindsInflectedForms_RankingTitleMatchesAboveAnnotationMatches()
    {
        await SeedCatalogAsync();

        // «міста» (genitive) matches the title «Місто» (weight A) and «рідного міста» in an annotation (weight B).
        var titles = await SearchTitlesAsync("міста");

        Assert.Equal(["Місто", "Ворошиловград"], titles);
    }

    [Fact]
    public async Task Search_FindsWordFormsInTheAnnotation()
    {
        await SeedCatalogAsync();

        Assert.Equal(["Дюна"], await SearchTitlesAsync("планета"));
    }

    [Fact]
    public async Task Search_FindsDuneByAnotherCase()
    {
        await SeedCatalogAsync();

        Assert.Equal(["Дюна"], await SearchTitlesAsync("дюну"));
    }

    [Theory]
    [InlineData("дюн")]     // partial word: substring match on the title
    [InlineData("Герберт")] // author name: not in the search vector
    public async Task Search_FindsPartialWordsAndAuthors(string query)
    {
        await SeedCatalogAsync();

        Assert.Equal(["Дюна"], await SearchTitlesAsync(query));
    }

    [Fact]
    public async Task Search_SupportsExcludedWords()
    {
        await SeedCatalogAsync();

        Assert.Equal(["Місто"], await SearchTitlesAsync("міста -рідного"));
    }

    [Fact]
    public async Task Filters_ByGenrePriceYearStockAndLanguage()
    {
        await using var db = Fixture.CreateDbContext();
        var author = new Author { FullName = "Автор" };
        var fantasy = new Genre { Name = "Фентезі" };
        var match = TestData.Book("Match", author, price: 200, year: 2000, genres: fantasy);
        db.Books.AddRange(
            match,
            TestData.Book("Too cheap", author, price: 50, year: 2000, genres: fantasy),
            TestData.Book("Too old", author, price: 200, year: 1900, genres: fantasy),
            TestData.Book("Out of stock", author, stock: 0, price: 200, year: 2000, genres: fantasy),
            TestData.Book("English", author, price: 200, year: 2000, language: "en", genres: fantasy),
            TestData.Book("Other genre", author, price: 200, year: 2000));
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).SearchAsync(
            Filter(genreIds: [fantasy.Id], minPrice: 100, maxPrice: 300, minYear: 1950, inStockOnly: true, language: "uk"),
            CancellationToken.None);

        Assert.Equal([match.Id], result.Items.Select(b => b.Id));
        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData(BookSort.PriceAscending, new[] { "B", "C", "A" })]
    [InlineData(BookSort.PriceDescending, new[] { "A", "C", "B" })]
    [InlineData(BookSort.Year, new[] { "C", "A", "B" })] // no year last
    [InlineData(BookSort.Title, new[] { "A", "B", "C" })]
    [InlineData(BookSort.Rating, new[] { "C", "A", "B" })] // unrated last
    public async Task Sorts(BookSort sort, string[] expected)
    {
        await using var db = Fixture.CreateDbContext();
        var author = new Author { FullName = "Автор" };
        var a = TestData.Book("A", author, price: 300, year: 1990);
        var b = TestData.Book("B", author, price: 100, year: null);
        var c = TestData.Book("C", author, price: 200, year: 2020);
        db.Books.AddRange(a, b, c);
        var user = await TestData.AddUserAsync(db);
        db.Ratings.AddRange(
            new Rating { UserId = user.Id, BookId = a.Id, Score = 3 },
            new Rating { UserId = user.Id, BookId = c.Id, Score = 5 });
        (a.RatingCount, a.RatingSum, c.RatingCount, c.RatingSum) = (1, 3, 1, 5); // as RatingService keeps them
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).SearchAsync(Filter(sort: sort), CancellationToken.None);

        Assert.Equal(expected, result.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task RatingSort_IsBayesian_ManyGoodRatingsBeatOneExcellentRating()
    {
        await using var db = Fixture.CreateDbContext();
        var author = new Author { FullName = "Автор" };
        var oneFive = TestData.Book("One five-star rating", author);
        var manyGood = TestData.Book("Two hundred 4.5-star ratings", author);
        var fewPoor = TestData.Book("Ten 2-star ratings", author);
        (oneFive.RatingCount, oneFive.RatingSum) = (1, 5);
        (manyGood.RatingCount, manyGood.RatingSum) = (200, 900);
        (fewPoor.RatingCount, fewPoor.RatingSum) = (10, 20);
        db.Books.AddRange(oneFive, manyGood, fewPoor, TestData.Book("Unrated", author));
        await db.SaveChangesAsync();

        var result = await new BookRepository(db).SearchAsync(Filter(sort: BookSort.Rating), CancellationToken.None);

        // C = 10, overall mean m = 925 / 211 ≈ 4.38; (sum + C·m) / (count + C):
        // 200 × 4.5 → (900 + 43.8) / 210 ≈ 4.49;  one 5 → (5 + 43.8) / 11 ≈ 4.44;  10 × 2 → (20 + 43.8) / 20 ≈ 3.19.
        Assert.Equal(
            ["Two hundred 4.5-star ratings", "One five-star rating", "Ten 2-star ratings", "Unrated"],
            result.Items.Select(i => i.Title));
    }

    [Theory]
    [InlineData("жАДа", new[] { "Сергій Жадан" })]   // part of the name, any case
    [InlineData("р", new[] { "Валер'ян Підмогильний", "Сергій Жадан", "Френк Герберт" })]
    [InlineData("100%", new string[0])]             // % is matched literally, not as a wildcard
    public async Task AuthorSearch_MatchesPartOfTheName(string search, string[] expected)
    {
        await SeedCatalogAsync();
        await using var db = Fixture.CreateDbContext();

        var result = await new AuthorRepository(db).GetPageAsync(search, 1, 20, CancellationToken.None);

        Assert.Equal(expected, result.Items.Select(a => a.FullName));
    }

    private async Task<IReadOnlyList<string>> SearchTitlesAsync(string search)
    {
        await using var db = Fixture.CreateDbContext();
        var result = await new BookRepository(db).SearchAsync(Filter(search: search), CancellationToken.None);
        return result.Items.Select(b => b.Title).ToList();
    }

    private async Task SeedCatalogAsync()
    {
        await using var db = Fixture.CreateDbContext();
        db.Books.AddRange(
            TestData.Book("Місто", new Author { FullName = "Валер'ян Підмогильний" },
                annotation: "Роман про сільського юнака, який підкорює Київ."),
            TestData.Book("Ворошиловград", new Author { FullName = "Сергій Жадан" },
                annotation: "Герман повертається до рідного міста, щоб урятувати бензоколонку брата."),
            TestData.Book("Дюна", new Author { FullName = "Френк Герберт" },
                annotation: "Боротьба за пустельну планету Арракіс."));
        await db.SaveChangesAsync();
    }

    private static BookFilter Filter(
        string? search = null,
        BookSort? sort = null,
        IReadOnlyCollection<Guid>? genreIds = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int? minYear = null,
        bool inStockOnly = false,
        string? language = null) =>
        new(search, genreIds, null, null, null, language, minPrice, maxPrice, minYear, null, inStockOnly, sort, 1, 20);
}
