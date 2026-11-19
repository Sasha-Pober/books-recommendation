using System.Net;
using System.Net.Http.Json;
using BookStore.Domain.Common;
using BookStore.Presentation.Common;
using BookStore.Presentation.Ratings;

namespace BookStore.IntegrationTests.Api;

public class RatingsApiTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task MyRating_IsEmptyUntilRated_ThenReturned_AndReviewsCanBeLimitedToWrittenOnes()
    {
        var book = TestData.Book("Кобзар", new BookStore.Domain.Entities.Author { FullName = "Тарас Шевченко" });
        await using (var db = Fixture.CreateDbContext())
        {
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var reader = await Fixture.Api.CreateClientAsAsync(Roles.Customer, "reader@example.com");
        var silent = await Fixture.Api.CreateClientAsAsync(Roles.Customer, "silent@example.com");

        Assert.Equal(HttpStatusCode.NoContent, (await reader.GetAsync($"/api/books/{book.Id}/ratings/mine")).StatusCode);

        (await reader.PostAsJsonAsync($"/api/books/{book.Id}/ratings", new { score = 5, reviewText = "Безсмертна книга" }))
            .EnsureSuccessStatusCode();
        (await silent.PostAsJsonAsync($"/api/books/{book.Id}/ratings", new { score = 4 })).EnsureSuccessStatusCode();

        var mine = await reader.GetFromJsonAsync<ReviewDto>($"/api/books/{book.Id}/ratings/mine");
        Assert.Equal((5, "Безсмертна книга"), (mine!.Score, mine.ReviewText));

        var all = await reader.GetFromJsonAsync<PagedResult<ReviewDto>>($"/api/books/{book.Id}/ratings");
        var written = await reader.GetFromJsonAsync<PagedResult<ReviewDto>>(
            $"/api/books/{book.Id}/ratings?withTextOnly=true");
        Assert.Equal(2, all!.TotalCount);
        Assert.Equal("Безсмертна книга", Assert.Single(written!.Items).ReviewText);

        var guest = await Fixture.Api.CreateClient().GetAsync($"/api/books/{book.Id}/ratings/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
    }
}
