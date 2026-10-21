using BookStore.Application.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Genres;

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<GenreDto> CreateAsync(CreateGenreRequest request, CancellationToken cancellationToken);
}

internal sealed class GenreService(IApplicationDbContext db) : IGenreService
{
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Genres
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name))
            .ToListAsync(cancellationToken);

    public async Task<GenreDto> CreateAsync(CreateGenreRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await db.Genres.AnyAsync(g => g.Name == name, cancellationToken))
        {
            throw new ConflictException($"Genre '{name}' already exists.");
        }

        var genre = new Genre { Name = name };
        db.Genres.Add(genre);
        await db.SaveChangesAsync(cancellationToken);

        return new GenreDto(genre.Id, genre.Name);
    }
}
