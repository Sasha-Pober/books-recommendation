using BookStore.Domain.Entities;
using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class RecommendationLogConfiguration : IEntityTypeConfiguration<RecommendationLog>
{
    public void Configure(EntityTypeBuilder<RecommendationLog> builder)
    {
        builder.Property(l => l.Placement).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(l => new { l.UserId, l.ShownAt });
        builder.HasIndex(l => l.ShownAt);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(l => l.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ModelVersion>()
            .WithMany()
            .HasForeignKey(l => l.ModelVersionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
