using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Genre : Entity
{
    public required string Name { get; set; }

    public ICollection<Book> Books { get; set; } = [];
}
