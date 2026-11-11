using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BookStore.Domain.Recommendations;
using BookStore.Domain.Services;
using BookStore.Infrastructure;
using BookStore.Infrastructure.Recommendations;
using BookStore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BookStore.UnitTests.Recommendations;

public class RecommendationServiceClientTests
{
    [Fact]
    public async Task Home_SendsSnakeCaseRequest_WithServiceKey_AndParsesResponse()
    {
        var handler = new FakeHandler(_ => Json("""
            {"model_version_id": 3, "items": [
              {"book_id": "01990000-0000-7000-8000-000000000001", "score": 0.91, "reason": "similar_to_viewed"},
              {"book_id": "01990000-0000-7000-8000-000000000002", "score": 0.5, "reason": "something_new"}
            ]}
            """));
        var engine = CreateEngine(handler);
        var userId = Guid.NewGuid();
        var excluded = Guid.NewGuid();

        var result = await engine.RecommendAsync(
            new HomeRecommendationRequest(userId, "session-1", 20, [excluded]), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://recsys.test/v1/recommendations/home", request.Uri);
        Assert.Equal("secret", request.ServiceKey);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal(userId.ToString(), body["user_id"]!.GetValue<string>());
        Assert.Equal("session-1", body["session_id"]!.GetValue<string>());
        Assert.Equal(20, body["count"]!.GetValue<int>());
        Assert.Equal(excluded.ToString(), body["exclude_book_ids"]![0]!.GetValue<string>());

        Assert.Equal(3, result.ModelVersionId);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(RecommendationReason.SimilarToViewed, result.Items[0].Reason);
        Assert.Null(result.Items[1].Reason); // unknown reason: no explanation, but the book is kept
    }

    [Fact]
    public async Task ServerError_Throws_SoTheCallerFallsBack()
    {
        var engine = CreateEngine(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            engine.FindSimilarAsync(Guid.NewGuid(), 5, [], CancellationToken.None));
    }

    [Fact]
    public async Task SlowService_IsCutOffByTimeout()
    {
        var engine = CreateEngine(new FakeHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Json("""{"items": []}""");
        }));

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            engine.FindSimilarAsync(Guid.NewGuid(), 5, [], CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuit()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var engine = CreateEngine(handler);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                engine.FindSimilarAsync(Guid.NewGuid(), 5, [], CancellationToken.None));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            engine.FindSimilarAsync(Guid.NewGuid(), 5, [], CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count); // the third call never reached the service
    }

    [Theory]
    [InlineData("liked_by_similar_readers", RecommendationReason.LikedBySimilarReaders)]
    [InlineData("NEW_FROM_FAVORITE_AUTHOR", RecommendationReason.NewFromFavoriteAuthor)]
    [InlineData("5", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseReason(string? value, RecommendationReason? expected) =>
        Assert.Equal(expected, RecommendationServiceClient.ParseReason(value));

    [Fact]
    public void WithoutBaseUrl_TheBaselineEngineIsUsed()
    {
        using var provider = BuildServices(baseUrl: null, handler: null);
        using var scope = provider.CreateScope();

        Assert.Equal("baseline", scope.ServiceProvider.GetRequiredService<IRecommendationEngine>().Name);
    }

    /// <summary>Resolved through DI, so the configured timeout and circuit breaker apply.</summary>
    private static IRecommendationEngine CreateEngine(FakeHandler handler)
    {
        var provider = BuildServices("http://recsys.test/", handler);
        return provider.CreateScope().ServiceProvider.GetRequiredService<IRecommendationEngine>();
    }

    private static ServiceProvider BuildServices(string? baseUrl, FakeHandler? handler)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:BookStore"] = "Host=localhost;Database=unused",
            ["Jwt:SigningKey"] = "test-signing-key-that-is-long-enough-0123456789",
            ["RecommendationService:BaseUrl"] = baseUrl,
            ["RecommendationService:ServiceKey"] = "secret",
            ["RecommendationService:Timeout"] = "00:00:00.200",
            ["RecommendationService:MinimumThroughput"] = "2",
            ["RecommendationService:FailureRatio"] = "0.5",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging().AddServices().AddInfrastructure(configuration);
        if (handler is not null)
        {
            services.AddHttpClient<RecommendationServiceClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Uri, string? ServiceKey, string Body);

    private sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(_ => Task.FromResult(respond(null!)))
        {
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues(RecommendationServiceClient.ServiceKeyHeader, out var key) ? key.Single() : null,
                body));
            return await respond(cancellationToken);
        }
    }
}
