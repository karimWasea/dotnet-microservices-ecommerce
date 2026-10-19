using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Application.Abstractions;
using OrderService.Application.Exceptions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.Infrastructure.HttpClients;

public sealed class ProductCatalogOptions
{
    public const string SectionName = "ProductCatalog";

    public string BaseUrl { get; set; } = "http://localhost:5001";
    public double TotalTimeoutSeconds { get; set; } = 8.0;
    public double AttemptTimeoutSeconds { get; set; } = 2.0;
    public int MaxRetryAttempts { get; set; } = 2;
    public int BaseRetryDelayMs { get; set; } = 200;
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
    public int CircuitBreakerMinimumThroughput { get; set; } = 6;
    public double CircuitBreakerSamplingDurationSeconds { get; set; } = 30.0;
    public double CircuitBreakerBreakDurationSeconds { get; set; } = 20.0;

    public void ValidateTimeoutBudget()
    {
        if (AttemptTimeoutSeconds <= 0 || TotalTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("ProductCatalog timeouts must be positive.");
        }

        var totalAttempts = MaxRetryAttempts + 1;
        var minRequiredBudget = AttemptTimeoutSeconds * totalAttempts;
        if (TotalTimeoutSeconds <= minRequiredBudget)
        {
            throw new InvalidOperationException(
                $"Invalid timeout budget: TotalTimeoutSeconds ({TotalTimeoutSeconds}s) must be greater than " +
                $"AttemptTimeoutSeconds ({AttemptTimeoutSeconds}s) * {totalAttempts} attempts ({minRequiredBudget}s).");
        }
    }
}

public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public CorrelationIdDelegatingHandler(ICorrelationIdAccessor correlationIdAccessor)
    {
        _correlationIdAccessor = correlationIdAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdAccessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(HeaderName))
        {
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ProductCatalogHttpClient : IProductCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ProductCatalogOptions _options;
    private readonly ILogger<ProductCatalogHttpClient> _logger;

    public ProductCatalogHttpClient(
        HttpClient httpClient,
        IOptions<ProductCatalogOptions> options,
        ILogger<ProductCatalogHttpClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/products/{productId}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if ((int)response.StatusCode >= 500 ||
                response.StatusCode == HttpStatusCode.RequestTimeout ||
                response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new DependencyUnavailableException(
                    "ProductService",
                    $"ProductService returned HTTP {(int)response.StatusCode}.",
                    (int)Math.Ceiling(_options.CircuitBreakerBreakDurationSeconds));
            }

            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<ProductApiResponse>(cancellationToken: ct);
            if (dto is null)
            {
                return null;
            }

            return new ProductSnapshot(
                dto.Id,
                dto.Name,
                dto.Price,
                string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency,
                dto.IsActive);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker is OPEN for ProductService when fetching ProductId={ProductId}", productId);
            throw new DependencyUnavailableException(
                "ProductService",
                "ProductService is temporarily unavailable (circuit breaker open).",
                (int)Math.Ceiling(_options.CircuitBreakerBreakDurationSeconds),
                ex);
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "Request to ProductService timed out for ProductId={ProductId}", productId);
            throw new DependencyTimeoutException(
                "ProductService",
                "Request to ProductService timed out.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP failure calling ProductService for ProductId={ProductId}", productId);
            throw new DependencyUnavailableException(
                "ProductService",
                "ProductService is currently unreachable.",
                (int)Math.Ceiling(_options.CircuitBreakerBreakDurationSeconds),
                ex);
        }
    }

    private sealed record ProductApiResponse(
        Guid Id,
        string Name,
        decimal Price,
        string? Currency,
        bool IsActive = true);
}

public static class ProductCatalogResilienceExtensions
{
    public static IHttpClientBuilder AddProductCatalogResiliencePipeline(
        this IHttpClientBuilder builder,
        ProductCatalogOptions options)
    {
        options.ValidateTimeoutBudget();

        builder.AddResilienceHandler("product-catalog", (resilienceBuilder, context) =>
        {
            var loggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("ProductCatalogResilience");

            // 1. Outer Total Timeout (8s)
            resilienceBuilder.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Name = "TotalTimeout",
                Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds)
            });

            // 2. Retry (max 2 retries, exponential backoff 200ms + jitter, GET only, never 404)
            if (options.MaxRetryAttempts > 0)
            {
                resilienceBuilder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "TransientRetry",
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(options.BaseRetryDelayMs),
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
                });
            }

            // 3. Circuit Breaker (50% failure ratio, min throughput 6, 30s sampling, 20s break, never 404)
            resilienceBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                Name = "ProductServiceCircuitBreaker",
                FailureRatio = options.CircuitBreakerFailureRatio,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
                OnOpened = args =>
                {
                    logger.LogWarning(
                        "ProductService circuit breaker OPENED for {BreakDurationSeconds}s",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("ProductService circuit breaker CLOSED");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogInformation("ProductService circuit breaker HALF-OPENED");
                    return ValueTask.CompletedTask;
                }
            });

            // 4. Inner Per-Attempt Timeout (2s)
            resilienceBuilder.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Name = "AttemptTimeout",
                Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds)
            });
        });

        return builder;
    }

    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Result?.RequestMessage is { Method: var method } && method != HttpMethod.Get)
        {
            return false;
        }

        return IsTransientFailure(outcome);
    }

    public static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Result is { StatusCode: var status })
        {
            if (status == HttpStatusCode.NotFound)
            {
                return false;
            }

            return (int)status >= 500 ||
                   status == HttpStatusCode.RequestTimeout ||
                   status == HttpStatusCode.TooManyRequests;
        }

        return false;
    }
}
