using BookStore.Domain.Entities;
using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        builder.Property(i => i.SessionId).HasMaxLength(100);

        // Stored as text so the data is self-describing for the Python training pipeline.
        builder.Property(i => i.EventType).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(i => new { i.UserId, i.CreatedAt });
        builder.HasIndex(i => i.BookId);
        builder.HasIndex(i => i.CreatedAt);
        // Attaching guest events on sign-in.
        builder.HasIndex(i => i.SessionId);

        // Guest events (UserId = null) keep their SessionId; deleting a user keeps the anonymized events.
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(i => i.Book)
            .WithMany()
            .HasForeignKey(i => i.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
