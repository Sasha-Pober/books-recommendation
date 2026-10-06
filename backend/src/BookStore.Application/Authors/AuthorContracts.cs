using System.ComponentModel.DataAnnotations;

namespace BookStore.Application.Authors;

public sealed record AuthorDto(Guid Id, string FullName, string? Bio);

public sealed record CreateAuthorRequest(
    [Required, MaxLength(200)] string FullName,
    [MaxLength(4000)] string? Bio);
