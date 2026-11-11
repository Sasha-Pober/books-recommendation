using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Services;
using BookStore.Presentation.Models;
using BookStore.Presentation.Recommendations;
using BookStore.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.IntegrationTests.Api;

public class AdminAndRecommendationsTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task AdminModels_ListsVersionsWithMetrics_AndRetrainIsUnavailableWithoutTheService()
    {
        await using (var db = Fixture.CreateDbContext())
        {
            db.ModelVersions.Add(new ModelVersion
            {
                Name = "hybrid-v1",
                TrainedAt = DateTime.UtcNow,
                MetricsJson = """{"ndcg@10": 0.31}""",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var admin = await Fixture.Api.CreateClientAsAsync(Roles.Admin, "admin@example.com");

        var models = await admin.GetFromJsonAsync<List<ModelVersionDto>>("/api/admin/models");
        var model = Assert.Single(models!);
        Assert.True(model.IsActive);
        Assert.Equal(0.31, model.Metrics!.Value.GetProperty("ndcg@10").GetDouble());

        var retrain = await admin.PostAsync("/api/admin/models/retrain", content: null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, retrain.StatusCode);
        Assert.NotNull(retrain.Headers.RetryAfter);
    }

    [Fact]
    public async Task AdminModels_AreForbiddenForCustomers()
    {
        var customer = await Fixture.Api.CreateClientAsAsync(Roles.Customer, "customer@example.com");

        var response = await customer.GetAsync("/api/admin/models");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Home_ForGuest_ServesTheFallback_LogsImpressions_AndCountsClicks()
    {
        await using (var db = Fixture.CreateDbContext())
        {
            var author = new Author { FullName = "Автор" };
            db.Books.AddRange(TestData.Book("Перша", author), TestData.Book("Друга", author));
            await db.SaveChangesAsync();
        }

        await RefreshCollectionsAsync();
        var client = Fixture.Api.CreateClient();

        var home = await client.GetFromJsonAsync<RecommendationListDto>("/api/recommendations/home?count=5", JsonOptions);

        Assert.Equal("baseline", home!.Source);
        Assert.Equal(2, home.Items.Count);
        Assert.All(home.Items, i => Assert.Equal("Новинка", i.Explanation));
        await using (var db = Fixture.CreateDbContext())
        {
            Assert.Equal(home.Items.Select(i => i.LogId!.Value).Order(), db.RecommendationLogs.Select(l => l.Id).Order());
        }

        // The user opens the first recommendation.
        var first = home.Items[0];
        var click = await client.PostAsJsonAsync(
            "/api/events",
            new { events = new[] { new { bookId = first.Book.Id, type = "View", recommendationLogId = first.LogId } } });
        Assert.Equal(HttpStatusCode.Accepted, click.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await using var db = Fixture.CreateDbContext();
            if (await db.RecommendationLogs.AnyAsync(l => l.Id == first.LogId && l.Clicked))
            {
                break;
            }

            Assert.True(DateTime.UtcNow < deadline, "The click was not recorded.");
            await Task.Delay(50);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>The cached collections were built from another test's data; rebuild them from this one's.</summary>
    private async Task RefreshCollectionsAsync()
    {
        await using var scope = Fixture.Api.Services.CreateAsyncScope();
        var builder = scope.ServiceProvider.GetRequiredService<RecommendationCollectionBuilder>();
        var cache = scope.ServiceProvider.GetRequiredService<IRecommendationCollectionCache>();
        foreach (var collection in Enum.GetValues<RecommendationCollection>())
        {
            await cache.SetAsync(collection, await builder.BuildAsync(collection, CancellationToken.None), CancellationToken.None);
        }
    }
}
