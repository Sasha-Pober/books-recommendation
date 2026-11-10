namespace BookStore.Domain.Exceptions;

/// <summary>Temporary overload; the client should retry later.</summary>
public sealed class ServiceUnavailableException(string message, TimeSpan retryAfter) : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
