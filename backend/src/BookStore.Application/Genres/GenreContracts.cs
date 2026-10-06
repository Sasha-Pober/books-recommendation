using System.ComponentModel.DataAnnotations;

namespace BookStore.Application.Genres;

public sealed record GenreDto(Guid Id, string Name);

public sealed record CreateGenreRequest([Required, MaxLength(100)] string Name);
