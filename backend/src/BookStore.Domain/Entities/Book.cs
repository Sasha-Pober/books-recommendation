using BookStore.Domain.Common;

namespace BookStore.Domain.Entities;

public class Book : Entity
{
    public required string Title { get; set; }

    public string? Isbn { get; set; }

    /// <summary>Used later by the ML service to build content (text) embeddings.</summary>
    public string? Description { get; set; }

    public int? PublicationYear { get; set; }

    public string? Language { get; set; }

    public string? CoverImageUrl { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public Guid AuthorId { get; set; }

    public Author Author { get; set; } = null!;

    public ICollection<Genre> Genres { get; set; } = [];

    public ICollection<Rating> Ratings { get; set; } = [];
}
