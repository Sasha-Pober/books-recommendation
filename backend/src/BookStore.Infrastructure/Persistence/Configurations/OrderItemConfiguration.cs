using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_order_items_quantity", "quantity > 0"));

        builder.HasKey(i => new { i.OrderId, i.BookId });
        builder.HasIndex(i => i.BookId);

        builder.Property(i => i.Price).HasPrecision(10, 2);

        // A book that was ordered can't be hard-deleted.
        builder.HasOne(i => i.Book)
            .WithMany()
            .HasForeignKey(i => i.BookId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
