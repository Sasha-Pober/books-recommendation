using BookStore.Domain.Common;
using BookStore.Domain.Entities;

namespace BookStore.UnitTests.Domain;

public class GenreTreeTests
{
    [Fact]
    public void WithDescendants_IncludesSubgenresAtAnyDepth_ButNotSiblingsOrParents()
    {
        var fiction = new Genre { Name = "Художня література" };
        var fantastic = new Genre { Name = "Фантастика", ParentId = fiction.Id };
        var fantasy = new Genre { Name = "Фентезі", ParentId = fantastic.Id };
        var urbanFantasy = new Genre { Name = "Міське фентезі", ParentId = fantasy.Id };
        var classics = new Genre { Name = "Класика", ParentId = fiction.Id };

        var result = GenreTree.WithDescendants([fiction, fantastic, fantasy, urbanFantasy, classics], fantastic.Id);

        Assert.Equal(new HashSet<Guid> { fantastic.Id, fantasy.Id, urbanFantasy.Id }, result);
    }

    [Fact]
    public void WithDescendants_TerminatesOnCycles()
    {
        var a = new Genre { Name = "A" };
        var b = new Genre { Name = "B", ParentId = a.Id };
        a.ParentId = b.Id;

        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, GenreTree.WithDescendants([a, b], a.Id));
    }
}
