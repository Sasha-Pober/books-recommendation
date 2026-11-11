using BookStore.Domain.Identity;
using BookStore.Infrastructure;
using BookStore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookStore.IntegrationTests;

/// <summary>The real service and infrastructure registrations on the test database, with a fixed caller.</summary>
internal static class TestServices
{
    public const string SigningKey = "integration-test-signing-key-0123456789-abcdef";

    public static ServiceProvider Build(string connectionString, Guid? userId, string sessionId = "session-1")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BookStore"] = connectionString,
                ["Jwt:SigningKey"] = SigningKey,
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddServices()
            .AddInfrastructure(configuration);
        services.Replace(ServiceDescriptor.Singleton<ICurrentUser>(new FixedCurrentUser(userId, sessionId)));
        return services.BuildServiceProvider();
    }

    private sealed record FixedCurrentUser(Guid? UserId, string SessionId) : ICurrentUser;
}
