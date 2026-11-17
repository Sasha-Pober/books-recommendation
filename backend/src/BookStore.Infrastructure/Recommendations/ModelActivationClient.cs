using System.Net;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Services;

namespace BookStore.Infrastructure.Recommendations;

/// <summary>
/// Asks the Python recommendation service to activate an imported model version:
/// <code>POST v1/models/{id}/activate → 200; 404 if the version or its files are unknown;
/// 503 (with Retry-After) if it can't load models now.</code>
/// Models are trained offline and imported with <c>recsys import-model</c>; this only switches between them.
/// </summary>
internal sealed class ModelActivationClient(HttpClient http) : IModelActivationService
{
    public async Task ActivateAsync(int modelVersionId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync($"v1/models/{modelVersionId}/activate", content: null, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(
                "The recommendation service is unreachable; try again later.", TimeSpan.FromSeconds(30));
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new NotFoundException("Model files", modelVersionId);
                case HttpStatusCode.ServiceUnavailable:
                    throw new ServiceUnavailableException(
                        "The recommendation service can't load models right now; try again later.",
                        response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
                default:
                    response.EnsureSuccessStatusCode();
                    break;
            }
        }
    }
}

/// <summary>Used when <c>RecommendationService:BaseUrl</c> is not set.</summary>
internal sealed class UnavailableModelActivation : IModelActivationService
{
    public Task ActivateAsync(int modelVersionId, CancellationToken cancellationToken) =>
        throw new ServiceUnavailableException(
            "The recommendation service is not configured (RecommendationService:BaseUrl).", TimeSpan.FromMinutes(5));
}
