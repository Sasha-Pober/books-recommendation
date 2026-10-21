namespace BookStore.Domain.Repositories;

/// <param name="Search">Case-insensitive match on title or author name.</param>
public sealed record BookFilter(string? Search, Guid? GenreId, Guid? AuthorId, int Page, int PageSize);
