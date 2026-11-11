namespace BookStore.Domain.Services;

/// <param name="JobId">Assigned by the recommendation service.</param>
/// <param name="Status">E.g. "queued", "running".</param>
public sealed record TrainingJob(string JobId, string Status);

/// <summary>Starts training of a new model in the recommendation service (it registers the result in ModelVersions).</summary>
public interface IModelTrainingService
{
    /// <exception cref="Exceptions.ConflictException">Training is already running.</exception>
    /// <exception cref="Exceptions.ServiceUnavailableException">The recommendation service is not configured or unreachable.</exception>
    Task<TrainingJob> StartTrainingAsync(CancellationToken cancellationToken);
}
