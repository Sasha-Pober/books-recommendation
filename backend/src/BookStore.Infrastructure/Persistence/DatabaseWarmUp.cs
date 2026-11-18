using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BookStore.Infrastructure.Persistence;

/// <summary>
/// At startup, opens as many connections as the pool keeps (Minimum Pool Size) so they are created, with the
/// dictionary loaded (see DbContextSetup), before users arrive; the pool keeps them open. Runs in the
/// background: the API starts serving immediately, and a database that isn't up yet only skips the warm-up.
/// </summary>
internal sealed class DatabaseWarmUp(
    IServiceScopeFactory scopeFactory,
    string connectionString,
    ILogger<DatabaseWarmUp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connections = Math.Max(new NpgsqlConnectionStringBuilder(connectionString).MinPoolSize, 1);
        var timer = Stopwatch.StartNew();
        try
        {
            // All at once, so the pool creates that many physical connections rather than reusing one.
            var scopes = Enumerable.Range(0, connections).Select(_ => scopeFactory.CreateAsyncScope()).ToList();
            try
            {
                await Task.WhenAll(scopes.Select(scope =>
                    scope.ServiceProvider.GetRequiredService<BookStoreDbContext>().Database.OpenConnectionAsync(stoppingToken)));
            }
            finally
            {
                foreach (var scope in scopes)
                {
                    await scope.DisposeAsync(); // returns the connections to the pool
                }
            }

            logger.LogInformation(
                "Database warm-up: {Count} connections ready in {Elapsed} ms", connections, timer.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Database warm-up failed; connections will warm up on first use");
        }
    }
}
