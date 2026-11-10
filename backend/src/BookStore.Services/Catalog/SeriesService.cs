using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Catalog;

namespace BookStore.Services.Catalog;

public interface ISeriesService
{
    Task<IReadOnlyList<SeriesDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<SeriesDto> CreateAsync(CreateSeriesRequest request, CancellationToken cancellationToken);
}

internal sealed class SeriesService(ISeriesRepository series, IUnitOfWork unitOfWork) : ISeriesService
{
    public async Task<IReadOnlyList<SeriesDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await series.GetAllAsync(cancellationToken);
        return all.Select(x => new SeriesDto(x.Id, x.Name)).ToList();
    }

    public async Task<SeriesDto> CreateAsync(CreateSeriesRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await series.ExistsByNameAsync(name, cancellationToken))
        {
            throw new ConflictException($"Series '{name}' already exists.");
        }

        var entity = new Series { Name = name };
        series.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SeriesDto(entity.Id, entity.Name);
    }
}
