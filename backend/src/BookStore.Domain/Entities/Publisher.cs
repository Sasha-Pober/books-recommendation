using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Publisher : Entity
{
    public required string Name { get; set; }
}
