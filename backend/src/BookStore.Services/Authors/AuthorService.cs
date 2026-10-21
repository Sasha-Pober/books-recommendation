using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Authors;
using BookStore.Presentation.Common;

namespace BookStore.Services.Authors;

public interface IAuthorService
{
    Task<PagedResult<AuthorDto>> GetAllAsync(PageQuery page, CancellationToken cancellationToken);

    Task<AuthorDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken);
}

internal sealed class AuthorService(IAuthorRepository authors, IUnitOfWork unitOfWork) : IAuthorService
{
    public async Task<PagedResult<AuthorDto>> GetAllAsync(PageQuery page, CancellationToken cancellationToken)
    {
        var result = await authors.GetPageAsync(page.Page, page.PageSize, cancellationToken);

        return new PagedResult<AuthorDto>(
            result.Items.Select(ToDto).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<AuthorDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var author = await authors.GetByIdAsync(id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Author), id);

        return ToDto(author);
    }

    public async Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken)
    {
        var author = new Author { FullName = request.FullName.Trim(), Bio = request.Bio };
        authors.Add(author);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(author);
    }

    private static AuthorDto ToDto(Author author) => new(author.Id, author.FullName, author.Bio);
}
