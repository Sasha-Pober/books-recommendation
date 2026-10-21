using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Series : Entity
{
    public required string Name { get; set; }
}
