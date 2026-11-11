using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence;

internal static class DbContextSetup
{
    /// <summary>PostgreSQL with pgvector and snake_case names; shared by the app and the integration tests.</summary>
    public static DbContextOptionsBuilder UseBookStoreDatabase(this DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention();
}
