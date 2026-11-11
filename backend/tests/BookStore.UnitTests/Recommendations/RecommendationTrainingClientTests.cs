using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BookStore.Domain.Exceptions;
using BookStore.Infrastructure.Recommendations;

namespace BookStore.UnitTests.Recommendations;

public class RecommendationTrainingClientTests
{
    [Fact]
    public async Task Accepted_ReturnsTheJob()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Accepted, """{"job_id": "job-1", "status": "queued"}"""));

        var job = await client.StartTrainingAsync(CancellationToken.None);

        Assert.Equal(("job-1", "queued"), (job.JobId, job.Status));
    }

    [Fact]
    public async Task Conflict_MeansTrainingIsAlreadyRunning()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

        await Assert.ThrowsAsync<ConflictException>(() => client.StartTrainingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ServiceUnavailable_IsReportedWithTheServicesRetryAfter()
    {
        var client = CreateClient(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(300));
            return response;
        });

        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.StartTrainingAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(300), error.RetryAfter);
    }

    [Fact]
    public async Task Unreachable_IsServiceUnavailable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => client.StartTrainingAsync(CancellationToken.None));
    }

    private static RecommendationTrainingClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://recsys.test/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
