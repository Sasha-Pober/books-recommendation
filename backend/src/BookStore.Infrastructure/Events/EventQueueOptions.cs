using System.ComponentModel.DataAnnotations;

namespace BookStore.Infrastructure.Events;

/// <summary>Configuration section "Events".</summary>
public sealed class EventQueueOptions
{
    public const string SectionName = "Events";

    /// <summary>Events held in memory before new ones are rejected with 503.</summary>
    [Range(100, 1_000_000)]
    public int Capacity { get; set; } = 10_000;

    /// <summary>Maximum events per database write.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 500;

    /// <summary>A partial batch is written after this delay.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Attempts for a batch that fails with a transient database error (e.g. connection lost).</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;
}
