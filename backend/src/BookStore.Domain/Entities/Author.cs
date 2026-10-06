using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Author : Entity
{
    public required string FullName { get; set; }

    public string? Bio { get; set; }

    public ICollection<Book> Books { get; set; } = [];
}
