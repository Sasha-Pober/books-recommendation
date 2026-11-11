using System.ComponentModel.DataAnnotations;

namespace BookStore.Infrastructure.Recommendations;

/// <summary>
/// Configuration section "RecommendationService": the Python (FastAPI) service.
/// Without <see cref="BaseUrl"/> the built-in baseline engine is used.
/// </summary>
public sealed class RecommendationServiceOptions : IValidatableObject
{
    public const string SectionName = "RecommendationService";

    /// <summary>E.g. http://recsys:8000/ (internal network only).</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Shared secret sent as the <c>X-Service-Key</c> header.</summary>
    public string? ServiceKey { get; set; }

    /// <summary>Per-request limit; the whole recommendation request must fit in 100 ms.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>Stop calling the service for <see cref="BreakDuration"/> when this share of recent calls failed.</summary>
    [Range(0.01, 1.0)]
    public double FailureRatio { get; set; } = 0.5;

    [Range(2, 1000)]
    public int MinimumThroughput { get; set; } = 10;

    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is not null && string.IsNullOrWhiteSpace(ServiceKey))
        {
            yield return new ValidationResult(
                "ServiceKey is required when BaseUrl is set.", [nameof(ServiceKey)]);
        }

        if (Timeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult("Timeout must be positive.", [nameof(Timeout)]);
        }
    }
}
