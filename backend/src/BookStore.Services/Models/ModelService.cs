using System.Text.Json;
using BookStore.Domain.Entities;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Presentation.Models;

namespace BookStore.Services.Models;

/// <summary>Recommendation model versions (Admin).</summary>
public interface IModelService
{
    Task<IReadOnlyList<ModelVersionDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<TrainingJobDto> RetrainAsync(CancellationToken cancellationToken);
}

internal sealed class ModelService(IModelVersionRepository models, IModelTrainingService training) : IModelService
{
    public async Task<IReadOnlyList<ModelVersionDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await models.GetAllAsync(cancellationToken);
        return all.Select(m => new ModelVersionDto(m.Id, m.Name, m.TrainedAt, ParseMetrics(m), m.IsActive)).ToList();
    }

    public async Task<TrainingJobDto> RetrainAsync(CancellationToken cancellationToken)
    {
        var job = await training.StartTrainingAsync(cancellationToken);
        return new TrainingJobDto(job.JobId, job.Status);
    }

    /// <summary>Written by the Python pipeline; invalid JSON is shown as no metrics rather than failing the list.</summary>
    private static JsonElement? ParseMetrics(ModelVersion model)
    {
        if (string.IsNullOrWhiteSpace(model.MetricsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(model.MetricsJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
