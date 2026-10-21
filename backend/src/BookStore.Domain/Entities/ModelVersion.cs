namespace BookStore.Domain.Entities;

/// <summary>A trained recommendation model. At most one version is active.</summary>
public class ModelVersion
{
    /// <summary>Database-generated.</summary>
    public int Id { get; set; }

    public required string Name { get; set; }

    public DateTime TrainedAt { get; set; }

    /// <summary>Offline evaluation results (NDCG@10, Recall@K, ...) as JSON.</summary>
    public string? MetricsJson { get; set; }

    public bool IsActive { get; set; }
}
