using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class BookEmbeddingConfiguration : IEntityTypeConfiguration<BookEmbedding>
{
    public void Configure(EntityTypeBuilder<BookEmbedding> builder)
    {
        builder.HasKey(e => new { e.BookId, e.ModelVersionId });

        // The domain uses float[] to stay free of the Pgvector dependency.
        builder.Property(e => e.Vector)
            .HasColumnType($"vector({BookEmbedding.Dimensions})")
            .HasConversion(
                v => new Vector(v),
                v => v.ToArray(),
                new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
                    v => v.ToArray()));

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ModelVersion)
            .WithMany()
            .HasForeignKey(e => e.ModelVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
