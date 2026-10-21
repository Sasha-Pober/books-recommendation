using BookStore.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence;

internal static class QueryableExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<T>(items, page, pageSize, total);
    }
}
