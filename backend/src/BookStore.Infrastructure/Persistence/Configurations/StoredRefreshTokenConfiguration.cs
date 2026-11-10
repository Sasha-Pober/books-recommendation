using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class StoredRefreshTokenConfiguration : IEntityTypeConfiguration<StoredRefreshToken>
{
    public void Configure(EntityTypeBuilder<StoredRefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.Property(t => t.TokenHash).HasMaxLength(64);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
