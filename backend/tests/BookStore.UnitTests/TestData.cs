using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BookStore.UnitTests;

internal static class TestData
{
    public static BookStoreDbContext CreateDbContext(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<BookStoreDbContext>();
        UseInMemory(builder, databaseName ?? Guid.NewGuid().ToString());
        return new BookStoreDbContext(builder.Options);
    }

    /// <summary>InMemory provider with the PostgreSQL-only columns removed. Same name = shared data.</summary>
    public static void UseInMemory(DbContextOptionsBuilder builder, string databaseName) =>
        builder
            .UseInMemoryDatabase(databaseName)
            .ReplaceService<IModelCustomizer, InMemoryModelCustomizer>();

    public static Book Book(string title, Author author, int stock = 5, params Genre[] genres) => new()
    {
        Title = title,
        Authors = [author],
        Stock = stock,
        Genres = genres.ToList(),
    };

    public static IEnumerable<Interaction> Interactions(Book book, InteractionType type, int times) =>
        Enumerable.Range(0, times).Select(_ => Interaction.Create(null, "session", book.Id, type));

    /// <summary>
    /// Removes PostgreSQL-only columns (tsvector, pgvector) the InMemory provider can't map.
    /// Goes away when these tests move to PostgreSQL via Testcontainers.
    /// </summary>
    private sealed class InMemoryModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Book>().Ignore(BookConfiguration.SearchVector);
            modelBuilder.Entity<BookEmbedding>().Ignore(e => e.Vector);
        }
    }
}
