namespace BookStore.Domain.Common;

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
