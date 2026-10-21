using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Genres;

namespace BookStore.Services.Genres;

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<GenreDto> CreateAsync(CreateGenreRequest request, CancellationToken cancellationToken);
}

internal sealed class GenreService(IGenreRepository genres, IUnitOfWork unitOfWork) : IGenreService
{
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await genres.GetAllAsync(cancellationToken);
        return all.Select(g => new GenreDto(g.Id, g.Name)).ToList();
    }

    public async Task<GenreDto> CreateAsync(CreateGenreRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await genres.ExistsByNameAsync(name, cancellationToken))
        {
            throw new ConflictException($"Genre '{name}' already exists.");
        }

        var genre = new Genre { Name = name };
        genres.Add(genre);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GenreDto(genre.Id, genre.Name);
    }
}
