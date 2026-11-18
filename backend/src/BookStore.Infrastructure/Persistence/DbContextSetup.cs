using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookStore.Infrastructure.Persistence;

internal static class DbContextSetup
{
    /// <summary>
    /// Loads the Ukrainian hunspell dictionary into a new database connection. PostgreSQL loads it once per
    /// connection on first use, which takes seconds, so it is done when the connection opens rather than in
    /// the first search a user runs on it. Selecting the configuration from pg_ts_config (instead of naming it)
    /// makes this a no-op on a database that has no such configuration yet, e.g. before the migrations run.
    /// </summary>
    private const string DictionaryWarmUp =
        $"SELECT to_tsvector(oid::regconfig, 'книга') FROM pg_ts_config WHERE cfgname = '{BookStoreDbContext.TextSearchConfig}'";

    /// <summary>PostgreSQL with pgvector and snake_case names; shared by the app and the integration tests.</summary>
    public static DbContextOptionsBuilder UseBookStoreDatabase(this DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseVector()
                .ConfigureDataSource(dataSource => dataSource.UsePhysicalConnectionInitializer(WarmUp, WarmUpAsync)))
            .UseSnakeCaseNamingConvention();

    private static void WarmUp(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(DictionaryWarmUp, connection);
        command.ExecuteNonQuery();
    }

    private static async Task WarmUpAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(DictionaryWarmUp, connection);
        await command.ExecuteNonQueryAsync();
    }
}
