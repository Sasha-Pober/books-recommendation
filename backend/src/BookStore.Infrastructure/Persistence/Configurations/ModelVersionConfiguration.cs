using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class ModelVersionConfiguration : IEntityTypeConfiguration<ModelVersion>
{
    public void Configure(EntityTypeBuilder<ModelVersion> builder)
    {
        builder.Property(m => m.Name).HasMaxLength(100);
        builder.Property(m => m.MetricsJson).HasColumnType("jsonb");

        builder.HasIndex(m => m.Name).IsUnique();

        // At most one active model.
        builder.HasIndex(m => m.IsActive).IsUnique().HasFilter("is_active");
    }
}
