using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence;

public sealed class BookStoreDbContext(DbContextOptions<BookStoreDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IUnitOfWork
{
    /// <summary>Name of the PostgreSQL text search configuration created by the initial migration.</summary>
    public const string TextSearchConfig = "ukrainian";

    public DbSet<Book> Books => Set<Book>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<Publisher> Publishers => Set<Publisher>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<BookEmbedding> BookEmbeddings => Set<BookEmbedding>();

    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    public DbSet<RecommendationLog> RecommendationLogs => Set<RecommendationLog>();

    public DbSet<StoredRefreshToken> RefreshTokens => Set<StoredRefreshToken>();

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // Changes tracked during the failed attempt must not leak into a later SaveChanges of this scope.
            ChangeTracker.Clear();
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Shorter names than the AspNet* defaults. Set before the other configurations,
        // so foreign keys to users are named after the final table name.
        modelBuilder.Entity<AppUser>().ToTable("users");
        modelBuilder.Entity<AppRole>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookStoreDbContext).Assembly);
    }
}
