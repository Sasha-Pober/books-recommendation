using System.Text.Json;
using BookStore.Domain.Entities;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Presentation.Models;

namespace BookStore.Services.Models;

/// <summary>Recommendation model versions (Admin).</summary>
public interface IModelService
{
    Task<IReadOnlyList<ModelVersionDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Makes an imported model version the one the recommendation service uses.</summary>
    Task<ModelVersionDto> ActivateAsync(int id, CancellationToken cancellationToken);
}

internal sealed class ModelService(IModelVersionRepository models, IModelActivationService activation) : IModelService
{
    public async Task<IReadOnlyList<ModelVersionDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await models.GetAllAsync(cancellationToken);
        return all.Select(ToDto).ToList();
    }

    public async Task<ModelVersionDto> ActivateAsync(int id, CancellationToken cancellationToken)
    {
        if (await models.GetByIdAsync(id, cancellationToken) is null)
        {
            throw new NotFoundException(nameof(ModelVersion), id);
        }

        // The service loads the model and updates model_versions.is_active itself.
        await activation.ActivateAsync(id, cancellationToken);

        var activated = await models.GetByIdAsync(id, cancellationToken)
                        ?? throw new NotFoundException(nameof(ModelVersion), id);
        return ToDto(activated);
    }

    private static ModelVersionDto ToDto(ModelVersion model) =>
        new(model.Id, model.Name, model.TrainedAt, ParseMetrics(model), model.IsActive);

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
