using BookStore.Domain.Entities;

namespace BookStore.Domain.Common;

public static class GenreTree
{
    /// <summary>The genre and all its subgenres at any depth. Cycles in the data are tolerated.</summary>
    public static IReadOnlySet<Guid> WithDescendants(IEnumerable<Genre> genres, Guid rootId)
    {
        var childrenByParent = genres
            .Where(g => g.ParentId is not null)
            .ToLookup(g => g.ParentId!.Value, g => g.Id);

        var result = new HashSet<Guid> { rootId };
        var pending = new Queue<Guid>([rootId]);
        while (pending.TryDequeue(out var id))
        {
            foreach (var child in childrenByParent[id])
            {
                if (result.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return result;
    }
}
