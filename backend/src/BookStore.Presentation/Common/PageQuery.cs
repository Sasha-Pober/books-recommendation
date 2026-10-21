using System.ComponentModel.DataAnnotations;

namespace BookStore.Application.Common.Models;

public record PageQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
