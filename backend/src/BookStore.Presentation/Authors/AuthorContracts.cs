using System.ComponentModel.DataAnnotations;
using BookStore.Presentation.Common;

namespace BookStore.Presentation.Authors;

public sealed record AuthorDto(Guid Id, string FullName, string? Bio);

public sealed record CreateAuthorRequest(
    [Required, MaxLength(200)] string FullName,
    [MaxLength(4000)] string? Bio);

public sealed record AuthorQuery : PageQuery
{
    /// <summary>A part of the author's name, case-insensitive (e.g. «костен» finds «Ліна Костенко»).</summary>
    [MaxLength(200)]
    public string? Search { get; init; }
}
