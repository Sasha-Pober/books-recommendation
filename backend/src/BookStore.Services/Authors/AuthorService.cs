using BookStore.Application.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Application.Authors;

public interface IAuthorService
{
    Task<PagedResult<AuthorDto>> GetAllAsync(PageQuery page, CancellationToken cancellationToken);

    Task<AuthorDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken);
}

internal sealed class AuthorService(IApplicationDbContext db) : IAuthorService
{
    public Task<PagedResult<AuthorDto>> GetAllAsync(PageQuery page, CancellationToken cancellationToken) =>
        db.Authors
            .AsNoTracking()
            .OrderBy(a => a.FullName)
            .Select(a => new AuthorDto(a.Id, a.FullName, a.Bio))
            .ToPagedResultAsync(page, cancellationToken);

    public async Task<AuthorDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Authors
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AuthorDto(a.Id, a.FullName, a.Bio))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Author), id);

    public async Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken)
    {
        var author = new Author { FullName = request.FullName.Trim(), Bio = request.Bio };
        db.Authors.Add(author);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthorDto(author.Id, author.FullName, author.Bio);
    }
}
