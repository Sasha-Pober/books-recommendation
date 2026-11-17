using System.Net;
using System.Net.Http.Headers;
using BookStore.Domain.Exceptions;
using BookStore.Infrastructure.Recommendations;

namespace BookStore.UnitTests.Recommendations;

public class ModelActivationClientTests
{
    [Fact]
    public async Task Ok_CallsTheVersionsActivateEndpoint()
    {
        Uri? called = null;
        var client = CreateClient(request =>
        {
            called = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.ActivateAsync(7, CancellationToken.None);

        Assert.Equal("http://recsys.test/v1/models/7/activate", called!.ToString());
    }

    [Fact]
    public async Task NotFound_MeansTheServiceHasNoFilesForTheVersion()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<NotFoundException>(() => client.ActivateAsync(7, CancellationToken.None));
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
            client.ActivateAsync(7, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(300), error.RetryAfter);
    }

    [Fact]
    public async Task Unreachable_IsServiceUnavailable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => client.ActivateAsync(7, CancellationToken.None));
    }

    private static ModelActivationClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://recsys.test/") });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
