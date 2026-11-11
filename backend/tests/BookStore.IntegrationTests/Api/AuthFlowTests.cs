using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Presentation.Auth;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Api;

public class AuthFlowTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Register_SignIn_RotateRefreshToken_AndDetectReuse()
    {
        var client = Fixture.Api.CreateClient();

        // Registration needs the Customer role, which only the migration creates outside development.
        var registered = await PostAsync<AuthResponse>(
            client, "/api/auth/register", new RegisterRequest("reader@example.com", "Reader123!", "Читач"));
        Assert.Equal([Roles.Customer], registered.User.Roles);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("reader@example.com", me!.Email);

        var refreshed = await PostAsync<AuthResponse>(
            client, "/api/auth/refresh", new RefreshTokenRequest(registered.RefreshToken));
        Assert.NotEqual(registered.RefreshToken, refreshed.RefreshToken);

        // Reusing the rotated token is rejected and also revokes the token issued in its place.
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(registered.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var afterReuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshed.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await Fixture.Api.CreateClientAsAsync(Roles.Customer, "customer@example.com");

        var response = await Fixture.Api.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest("customer@example.com", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GuestViews_AreAttachedToTheAccount_OnRegistration()
    {
        Guid bookId;
        await using (var db = Fixture.CreateDbContext())
        {
            var book = TestData.Book("Дюна", new Author { FullName = "Френк Герберт" });
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        // The same client keeps the guest session cookie across requests.
        var client = Fixture.Api.CreateClient();
        var view = await client.PostAsJsonAsync("/api/events", new { events = new[] { new { bookId, type = "View" } } });
        Assert.Equal(HttpStatusCode.Accepted, view.StatusCode);
        await WaitForAsync(async db => await db.Interactions.AnyAsync(i => i.BookId == bookId));

        var registered = await PostAsync<AuthResponse>(
            client, "/api/auth/register", new RegisterRequest("guest@example.com", "Reader123!", "Гість"));

        await using var check = Fixture.CreateDbContext();
        var interaction = await check.Interactions.SingleAsync(i => i.BookId == bookId);
        Assert.Equal(registered.User.Id, interaction.UserId);
    }

    private async Task WaitForAsync(Func<BookStore.Infrastructure.Persistence.BookStoreDbContext, Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await using var db = Fixture.CreateDbContext();
            if (await condition(db))
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the event writer.");
            await Task.Delay(50);
        }
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
