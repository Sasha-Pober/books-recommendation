using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.Property(b => b.Title).HasMaxLength(300);
        builder.Property(b => b.Isbn).HasMaxLength(20);
        builder.Property(b => b.Language).HasMaxLength(10);
        builder.Property(b => b.CoverImageUrl).HasMaxLength(2048);
        builder.Property(b => b.Price).HasPrecision(10, 2);

        builder.HasIndex(b => b.Isbn).IsUnique();
        builder.HasIndex(b => b.Title);

        builder.HasOne(b => b.Author)
            .WithMany(a => a.Books)
            .HasForeignKey(b => b.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Genres)
            .WithMany(g => g.Books)
            .UsingEntity(
                "book_genres",
                right => right.HasOne(typeof(Genre)).WithMany().HasForeignKey("GenreId"),
                left => left.HasOne(typeof(Book)).WithMany().HasForeignKey("BookId"),
                join => join.HasKey("BookId", "GenreId"));
    }
}
