using BookStore.Domain.Recommendations;
using BookStore.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookStore.Services.Recommendations;

/// <summary>
/// Recomputes the non-personalized collections periodically and stores them in the cache, so the fallback
/// path is a cache read rather than aggregate queries while the recommendation service is down.
/// </summary>
internal sealed class RecommendationCollectionRefresher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RecommendationCollectionRefresher> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var builder = scope.ServiceProvider.GetRequiredService<RecommendationCollectionBuilder>();
            var cache = scope.ServiceProvider.GetRequiredService<IRecommendationCollectionCache>();

            foreach (var collection in Enum.GetValues<RecommendationCollection>())
            {
                var items = await builder.BuildAsync(collection, cancellationToken);
                await cache.SetAsync(collection, items, cancellationToken);
                logger.LogDebug("Refreshed {Collection}: {Count} books", collection, items.Count);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the previous cached collections; try again on the next tick.
            logger.LogWarning(ex, "Refreshing recommendation collections failed");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
