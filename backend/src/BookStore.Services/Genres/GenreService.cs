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
        return all.Select(ToDto).ToList();
    }

    public async Task<GenreDto> CreateAsync(CreateGenreRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await genres.ExistsByNameAsync(name, cancellationToken))
        {
            throw new ConflictException($"Genre '{name}' already exists.");
        }

        if (request.ParentId is { } parentId && !await genres.ExistsAsync(parentId, cancellationToken))
        {
            throw new ValidationException($"Parent genre '{parentId}' does not exist.");
        }

        var genre = new Genre { Name = name, ParentId = request.ParentId };
        genres.Add(genre);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(genre);
    }

    private static GenreDto ToDto(Genre genre) => new(genre.Id, genre.Name, genre.ParentId);
}
