using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class User : Entity
{
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public ICollection<Rating> Ratings { get; set; } = [];

    public ICollection<Interaction> Interactions { get; set; } = [];
}
