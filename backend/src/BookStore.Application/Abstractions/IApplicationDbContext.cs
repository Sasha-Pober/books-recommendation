using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Book> Books { get; }

    DbSet<Author> Authors { get; }

    DbSet<Genre> Genres { get; }

    DbSet<User> Users { get; }

    DbSet<Rating> Ratings { get; }

    DbSet<Interaction> Interactions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
