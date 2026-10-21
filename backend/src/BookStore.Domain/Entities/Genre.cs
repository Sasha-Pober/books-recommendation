using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

/// <summary>Genres form a hierarchy, e.g. "Фантастика" → "Фентезі".</summary>
public class Genre : Entity
{
    public required string Name { get; set; }

    public Guid? ParentId { get; set; }

    public Genre? Parent { get; set; }

    public ICollection<Genre> Children { get; set; } = [];

    public ICollection<Book> Books { get; set; } = [];
}
