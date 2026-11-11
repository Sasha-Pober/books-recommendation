using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Services;

namespace BookStore.Infrastructure.Recommendations;

/// <summary>
/// Starts training in the Python recommendation service:
/// <code>POST v1/training/jobs → 202 {"job_id": "...", "status": "queued"}; 409 if training is already running.</code>
/// The pipeline itself writes the new model to <c>model_versions</c> and activates it when it beats the current one.
/// </summary>
internal sealed class RecommendationTrainingClient(HttpClient http) : IModelTrainingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<TrainingJob> StartTrainingAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync("v1/training/jobs", content: null, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(
                "The recommendation service is unreachable; try again later.", TimeSpan.FromSeconds(30));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new ConflictException("Training is already running.");
            }

            response.EnsureSuccessStatusCode();
            var job = await response.Content.ReadFromJsonAsync<TrainingJob>(JsonOptions, cancellationToken);
            return job is { JobId.Length: > 0 }
                ? job
                : throw new InvalidDataException("The recommendation service returned no job id.");
        }
    }
}

/// <summary>Used when <c>RecommendationService:BaseUrl</c> is not set.</summary>
internal sealed class UnavailableTrainingService : IModelTrainingService
{
    public Task<TrainingJob> StartTrainingAsync(CancellationToken cancellationToken) =>
        throw new ServiceUnavailableException(
            "The recommendation service is not configured (RecommendationService:BaseUrl).", TimeSpan.FromMinutes(5));
}
