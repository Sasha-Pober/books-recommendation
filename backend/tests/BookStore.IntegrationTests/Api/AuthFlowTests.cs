using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Presentation.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BookStore.IntegrationTests.Api;

public class AuthFlowTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Register_SignIn_RotateRefreshToken_AndDetectReuse()
    {
        // Cookies are handled by hand here, to replay an old refresh token.
        var client = Fixture.Api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Registration needs the Customer role, which only the migration creates outside development.
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("reader@example.com", "Reader123!", "Читач"));
        var registered = await ReadAsync<SessionResponse>(registration);
        Assert.Equal([Roles.Customer], registered.User.Roles);
        var firstToken = RefreshCookie(registration);

        // The refresh token is only in the cookie, never readable by page scripts.
        var cookie = Assert.Single(registration.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bookstore_refresh="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstToken, await registration.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("reader@example.com", me!.Email);

        var refresh = await RefreshAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("reader@example.com", (await ReadAsync<SessionResponse>(refresh)).User.Email);
        var secondToken = RefreshCookie(refresh);
        Assert.NotEqual(firstToken, secondToken);

        // Reusing the rotated token is rejected (and the cookie deleted); it also revokes the token issued in its place.
        var reuse = await RefreshAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("", RefreshCookie(reuse));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, secondToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken_AndDeletesTheCookie()
    {
        var client = Fixture.Api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest("leaving@example.com", "Reader123!", "Читач"));
        var token = RefreshCookie(registration);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", $"bookstore_refresh={token}");
        var response = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", RefreshCookie(response));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, token)).StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var response = await Fixture.Api.CreateClient().PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

        var registered = await PostAsync<SessionResponse>(
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

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"bookstore_refresh={token}");
        return await client.SendAsync(request);
    }

    /// <summary>Value of the refresh token cookie set by the response ("" when it deletes the cookie).</summary>
    private static string RefreshCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bookstore_refresh="));
        return cookie["bookstore_refresh=".Length..cookie.IndexOf(';')];
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
