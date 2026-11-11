using BookStore.Domain.Common;
using BookStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

/// <summary>Roles are reference data: created by a migration in every environment (registration needs Customer).</summary>
internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    // Fixed values: seed data must not change between model builds.
    private static readonly (string Name, Guid Id)[] Seed =
    [
        (Roles.Customer, Guid.Parse("01990000-0000-7000-8000-00000000c001")),
        (Roles.Manager, Guid.Parse("01990000-0000-7000-8000-00000000c002")),
        (Roles.Admin, Guid.Parse("01990000-0000-7000-8000-00000000c003")),
    ];

    public void Configure(EntityTypeBuilder<AppRole> builder) =>
        builder.HasData(Seed.Select(r => new AppRole
        {
            Id = r.Id,
            Name = r.Name,
            NormalizedName = r.Name.ToUpperInvariant(),
            ConcurrencyStamp = r.Id.ToString(),
        }));
}
