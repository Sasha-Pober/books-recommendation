using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    /// <summary>Shadow property: infrastructure detail, not part of the domain model.</summary>
    public const string SearchVector = "SearchVector";

    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_books_price", "price >= 0");
            t.HasCheckConstraint("ck_books_stock", "stock >= 0");
        });

        builder.Property(b => b.Title).HasMaxLength(300);
        builder.Property(b => b.Isbn).HasMaxLength(20);
        builder.Property(b => b.Language).HasMaxLength(10);
        builder.Property(b => b.CoverImageUrl).HasMaxLength(2048);
        builder.Property(b => b.Price).HasPrecision(10, 2);

        builder.HasIndex(b => b.Isbn).IsUnique();
        builder.HasIndex(b => b.Title);

        // Full-text search: title (weight A) + annotation (weight B), maintained by PostgreSQL.
        builder.Property<NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql(
                $"setweight(to_tsvector('{BookStoreDbContext.TextSearchConfig}'::regconfig, coalesce(title, '')), 'A') || " +
                $"setweight(to_tsvector('{BookStoreDbContext.TextSearchConfig}'::regconfig, coalesce(annotation, '')), 'B')",
                stored: true);
        builder.HasIndex(SearchVector).HasMethod("gin");

        builder.HasOne(b => b.Publisher)
            .WithMany()
            .HasForeignKey(b => b.PublisherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Series)
            .WithMany()
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Authors)
            .WithMany(a => a.Books)
            .UsingEntity(
                "book_authors",
                right => right.HasOne(typeof(Author)).WithMany().HasForeignKey("AuthorId"),
                left => left.HasOne(typeof(Book)).WithMany().HasForeignKey("BookId"),
                join => join.HasKey("BookId", "AuthorId"));

        builder.HasMany(b => b.Genres)
            .WithMany(g => g.Books)
            .UsingEntity(
                "book_genres",
                right => right.HasOne(typeof(Genre)).WithMany().HasForeignKey("GenreId"),
                left => left.HasOne(typeof(Book)).WithMany().HasForeignKey("BookId"),
                join => join.HasKey("BookId", "GenreId"));
    }
}
