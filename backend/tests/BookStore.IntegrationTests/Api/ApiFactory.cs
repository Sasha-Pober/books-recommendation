using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookStore.Infrastructure.Identity;
using BookStore.Presentation.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Api;

/// <summary>The whole API in memory, on the test database (environment "Testing": no demo seeding).</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BookStore", connectionString);
        builder.UseSetting("Jwt:SigningKey", TestServices.SigningKey);
        builder.UseSetting("Events:FlushInterval", "00:00:00.050");
    }

    /// <summary>Creates an account with the given role (roles come from the migration) and returns a signed-in client.</summary>
    public async Task<HttpClient> CreateClientAsAsync(string role, string email)
    {
        const string password = "Passw0rd!";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, FullName = role };
            Check(await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role));
        }

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static void Check(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
