namespace BookStore.Domain.Common;

public abstract class Entity<TId>
    where TId : struct
{
    public TId Id { get; protected set; }

    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
}

/// <summary>
/// Default base for entities: time-ordered Guid (v7) key generated in code,
/// so ids are known before saving and not guessable through the API.
/// </summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity() => Id = Guid.CreateVersion7();
}
