using BookStore.Infrastructure.Persistence;
using BookStore.IntegrationTests.Api;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BookStore.IntegrationTests;

/// <summary>
/// One PostgreSQL container per test run, built from infra/postgres (pgvector + Ukrainian dictionary),
/// so the tests run against the same database as the app. Migrations are applied once.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private IFutureDockerImage? _image;
    private PostgreSqlContainer? _container;
    private ApiFactory? _api;

    public string ConnectionString => _container?.GetConnectionString()
                                      ?? throw new InvalidOperationException("The container is not started.");

    public async Task InitializeAsync()
    {
        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetGitDirectory(), "infra/postgres")
            .WithName("bookstore-postgres-tests:latest")
            .WithCleanUp(false) // keep the image (and its layer cache) between runs
            .Build();
        await _image.CreateAsync();

        _container = new PostgreSqlBuilder(_image).Build();
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>The API on this database; started on first use and shared by the API tests.</summary>
    public ApiFactory Api => _api ??= new ApiFactory(ConnectionString);

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public BookStoreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BookStoreDbContext>();
        options.UseBookStoreDatabase(ConnectionString);
        return new BookStoreDbContext(options.Options);
    }

    /// <summary>Empties every table except the migrations history and the roles seeded by a migration.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            """
            SELECT quote_ident(tablename) FROM pg_tables
            WHERE schemaname = 'public' AND tablename NOT IN ('__EFMigrationsHistory', 'roles')
            """, connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        await using var truncate = new NpgsqlCommand(
            $"TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY CASCADE", connection);
        await truncate.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>Base class: tests in the collection run one at a time, each on empty tables.</summary>
[Collection(PostgresCollection.Name)]
public abstract class PostgresTest(PostgresFixture fixture) : IAsyncLifetime
{
    protected PostgresFixture Fixture { get; } = fixture;

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
