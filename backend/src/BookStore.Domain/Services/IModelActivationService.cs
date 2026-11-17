namespace BookStore.Domain.Services;

/// <summary>
/// Switches the recommendation service to another imported model version. The service checks that the
/// version's files are present, loads it and marks it active in model_versions.
/// </summary>
public interface IModelActivationService
{
    /// <exception cref="Exceptions.NotFoundException">The service has no files for this version.</exception>
    /// <exception cref="Exceptions.ServiceUnavailableException">The recommendation service is not configured or unreachable.</exception>
    Task ActivateAsync(int modelVersionId, CancellationToken cancellationToken);
}
