using System.ComponentModel.DataAnnotations;

namespace BookStore.Presentation.Genres;

/// <param name="ParentId">Null for top-level genres.</param>
public sealed record GenreDto(Guid Id, string Name, Guid? ParentId);

public sealed record CreateGenreRequest([Required, MaxLength(100)] string Name, Guid? ParentId);
