using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Infrastructure;

/// <summary>Database objects created by the migrations, and the constraints the model relies on.</summary>
public class DatabaseTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Migrations_CreateUkrainianSearchConfiguration_AndRoles()
    {
        await using var db = Fixture.CreateDbContext();

        var configs = await db.Database
            .SqlQuery<string>($"SELECT cfgname::text AS \"Value\" FROM pg_ts_config WHERE cfgname = 'ukrainian'")
            .ToListAsync();
        Assert.Single(configs);

        var roles = await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(["Admin", "Customer", "Manager"], roles);
    }

    [Fact]
    public async Task UkrainianDictionary_ReducesWordFormsToTheSameLexeme()
    {
        await using var db = Fixture.CreateDbContext();

        var matches = await db.Database
            .SqlQuery<bool>($"SELECT to_tsvector('ukrainian', 'Місто') @@ to_tsquery('ukrainian', 'міста') AS \"Value\"")
            .SingleAsync();

        Assert.True(matches);
    }

    [Fact]
    public async Task RatingScore_OutsideOneToFive_IsRejected()
    {
        await using var db = Fixture.CreateDbContext();
        var user = await TestData.AddUserAsync(db);
        var book = TestData.Book("Книга", new Author { FullName = "Автор" });
        db.Books.Add(book);
        await db.SaveChangesAsync();

        db.Ratings.Add(new Rating { UserId = user.Id, BookId = book.Id, Score = 6 });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_ratings_score", error.InnerException?.Message);
    }

    [Fact]
    public async Task BookEmbedding_RoundTripsA384DimensionalVector()
    {
        var vector = Enumerable.Range(0, BookEmbedding.Dimensions).Select(i => i / 1000f).ToArray();
        Guid bookId;
        await using (var db = Fixture.CreateDbContext())
        {
            var book = TestData.Book("Книга", new Author { FullName = "Автор" });
            var model = new ModelVersion { Name = "hybrid-v1", TrainedAt = DateTime.UtcNow };
            db.AddRange(book, model);
            await db.SaveChangesAsync();
            db.BookEmbeddings.Add(new BookEmbedding { BookId = book.Id, ModelVersionId = model.Id, Vector = vector });
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        await using var check = Fixture.CreateDbContext();
        var stored = await check.BookEmbeddings.SingleAsync(e => e.BookId == bookId);
        Assert.Equal(vector, stored.Vector);
    }

    [Fact]
    public async Task OnlyOneModelVersion_CanBeActive()
    {
        await using var db = Fixture.CreateDbContext();
        db.ModelVersions.Add(new ModelVersion { Name = "v1", TrainedAt = DateTime.UtcNow, IsActive = true });
        await db.SaveChangesAsync();

        db.ModelVersions.Add(new ModelVersion { Name = "v2", TrainedAt = DateTime.UtcNow, IsActive = true });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
