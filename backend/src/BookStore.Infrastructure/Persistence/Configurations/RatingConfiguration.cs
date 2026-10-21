using BookStore.Domain.Entities;
using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_ratings_score", $"score BETWEEN {Rating.MinScore} AND {Rating.MaxScore}"));

        builder.HasKey(r => new { r.UserId, r.BookId });
        builder.HasIndex(r => r.BookId);

        builder.Property(r => r.ReviewText).HasMaxLength(4000);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Book)
            .WithMany()
            .HasForeignKey(r => r.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
