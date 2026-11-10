using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Catalog;

namespace BookStore.Services.Catalog;

public interface IPublisherService
{
    Task<IReadOnlyList<PublisherDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<PublisherDto> CreateAsync(CreatePublisherRequest request, CancellationToken cancellationToken);
}

internal sealed class PublisherService(IPublisherRepository publishers, IUnitOfWork unitOfWork) : IPublisherService
{
    public async Task<IReadOnlyList<PublisherDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await publishers.GetAllAsync(cancellationToken);
        return all.Select(x => new PublisherDto(x.Id, x.Name)).ToList();
    }

    public async Task<PublisherDto> CreateAsync(CreatePublisherRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await publishers.ExistsByNameAsync(name, cancellationToken))
        {
            throw new ConflictException($"Publisher '{name}' already exists.");
        }

        var entity = new Publisher { Name = name };
        publishers.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PublisherDto(entity.Id, entity.Name);
    }
}
