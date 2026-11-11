using System.Text.Json;

namespace BookStore.Presentation.Models;

/// <param name="Metrics">Offline evaluation results as written by the training pipeline (e.g. {"ndcg@10": 0.31}).</param>
public sealed record ModelVersionDto(int Id, string Name, DateTime TrainedAt, JsonElement? Metrics, bool IsActive);

public sealed record TrainingJobDto(string JobId, string Status);
