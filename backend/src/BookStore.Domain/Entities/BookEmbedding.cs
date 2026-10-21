namespace BookStore.Domain.Entities;

/// <summary>
/// Content vector of a book produced by a model version. Written by the Python training pipeline.
/// Key: (BookId, ModelVersionId).
/// </summary>
public class BookEmbedding
{
    /// <summary>Output size of paraphrase-multilingual-MiniLM-L12-v2.</summary>
    public const int Dimensions = 384;

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int ModelVersionId { get; set; }

    public ModelVersion ModelVersion { get; set; } = null!;

    public float[] Vector { get; set; } = [];
}
