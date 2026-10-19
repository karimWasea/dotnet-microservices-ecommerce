using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderService.Application.Exceptions;
using OrderService.Infrastructure.HttpClients;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.UnitTests.Infrastructure;

public sealed class ResilienceTests
{
    [Fact]
    public void ValidateTimeoutBudget_WhenBudgetTooSmall_ThrowsInvalidOperationException()
    {
        var options = new ProductCatalogOptions
        {
            TotalTimeoutSeconds = 5.0,
            AttemptTimeoutSeconds = 2.0,
            MaxRetryAttempts = 2 // 2.0 * (2 + 1) = 6.0 > 5.0 -> invalid!
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateTimeoutBudget());
    }

    [Fact]
    public void ValidateTimeoutBudget_WhenBudgetSufficient_DoesNotThrow()
    {
        var options = new ProductCatalogOptions
        {
            TotalTimeoutSeconds = 8.0,
            AttemptTimeoutSeconds = 2.0,
            MaxRetryAttempts = 2 // 2.0 * 3 = 6.0 < 8.0 -> valid
        };

        options.ValidateTimeoutBudget();
    }

    [Fact]
    public async Task ProductCatalogHttpClient_When404_ReturnsNullWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        var options = Options.Create(new ProductCatalogOptions());
        var catalogClient = new ProductCatalogHttpClient(client, options, NullLogger<ProductCatalogHttpClient>.Instance);

        var result = await catalogClient.GetProductAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void ShouldRetry_OnlyRetriesGetAndTransientFailures()
    {
        // 404 should NOT retry
        var reqGet = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5001/api/products/1");
        var resp404 = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = reqGet };
        Assert.False(ProductCatalogResilienceExtensions.ShouldRetry(Outcome.FromResult(resp404)));

        // 503 should retry
        var resp503 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = reqGet };
        Assert.True(ProductCatalogResilienceExtensions.ShouldRetry(Outcome.FromResult(resp503)));

        // 500 POST should NOT retry (non-idempotent)
        var reqPost = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5001/api/products");
        var resp500Post = new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = reqPost };
        Assert.False(ProductCatalogResilienceExtensions.ShouldRetry(Outcome.FromResult(resp500Post)));

        // Network exception on GET should retry
        var outcomeEx = Outcome.FromException<HttpResponseMessage>(new HttpRequestException("network down"));
        Assert.True(ProductCatalogResilienceExtensions.IsTransientFailure(outcomeEx));
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
