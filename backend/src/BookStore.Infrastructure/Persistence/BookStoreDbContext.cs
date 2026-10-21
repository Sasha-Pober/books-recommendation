using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence;

public sealed class BookStoreDbContext(DbContextOptions<BookStoreDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookStoreDbContext).Assembly);
}
