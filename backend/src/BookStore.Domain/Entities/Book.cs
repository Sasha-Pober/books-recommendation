using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Book : Entity
{
    public required string Title { get; set; }

    public string? Isbn { get; set; }

    /// <summary>Together with the title, the input of the content (text) embedding.</summary>
    public string? Annotation { get; set; }

    public Guid? PublisherId { get; set; }

    public Publisher? Publisher { get; set; }

    public Guid? SeriesId { get; set; }

    public Series? Series { get; set; }

    /// <summary>ISO 639-1 code, e.g. "uk".</summary>
    public string? Language { get; set; }

    public int? Year { get; set; }

    public int? Pages { get; set; }

    public string? CoverImageUrl { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public ICollection<Author> Authors { get; set; } = [];

    public ICollection<Genre> Genres { get; set; } = [];
}
