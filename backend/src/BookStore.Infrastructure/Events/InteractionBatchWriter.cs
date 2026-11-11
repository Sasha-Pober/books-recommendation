using BookStore.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Events;

/// <summary>
/// Reads queued events and writes them in batches: when <see cref="EventQueueOptions.BatchSize"/> events
/// have arrived, or <see cref="EventQueueOptions.FlushInterval"/> after the first event of a batch.
/// On shutdown it stops accepting events and writes everything still queued.
/// </summary>
internal sealed class InteractionBatchWriter(
    ChannelInteractionQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<EventQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<InteractionBatchWriter> logger) : BackgroundService
{
    private readonly EventQueueOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = queue.Reader;
        var batch = new List<QueuedView>(_options.BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                using var flushTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                flushTimer.CancelAfter(_options.FlushInterval);
                try
                {
                    while (batch.Count < _options.BatchSize)
                    {
                        if (reader.TryRead(out var view))
                        {
                            batch.Add(view);
                        }
                        else if (!await reader.WaitToReadAsync(flushTimer.Token))
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Flush interval elapsed with a partial batch.
                }

                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: drain below.
        }

        queue.Complete();
        while (reader.TryRead(out var view))
        {
            batch.Add(view);
            if (batch.Count >= _options.BatchSize)
            {
                await FlushAsync(batch);
            }
        }

        await FlushAsync(batch);
        logger.LogInformation("Event writer stopped");
    }

    /// <summary>Writes and clears the batch. Not cancellable, so shutdown never abandons a batch mid-write.</summary>
    private async Task FlushAsync(List<QueuedView> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var saved = await scope.ServiceProvider.GetRequiredService<InteractionBatchStore>()
                    .SaveAsync(batch, CancellationToken.None);
                logger.LogDebug("Saved {Saved} of {Count} events", saved, batch.Count);
                break;
            }
            catch (Exception ex) when (InteractionBatchStore.IsTransient(ex) && attempt < _options.MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "Saving {Count} events failed (attempt {Attempt}); retrying in {Delay}",
                    batch.Count, attempt, delay);
                await Task.Delay(delay, timeProvider);
            }
            catch (Exception ex)
            {
                // The writer must keep running; losing a batch of views is acceptable.
                logger.LogError(ex, "Dropped {Count} events after {Attempt} attempts", batch.Count, attempt);
                break;
            }
        }

        batch.Clear();
    }
}
