using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using ProductService.Application.Exceptions;
using ProductService.Application.Monitoring;
using ProductService.Domain.Abstractions;

namespace ProductService.Application.Behaviors;

public sealed class CircuitBreakerRegistry
{
    public ResiliencePipeline Pipeline { get; }

    public CircuitBreakerRegistry(
        CircuitBreakerOptions options,
        ProductMetrics metrics,
        TimeProvider timeProvider,
        ILogger<CircuitBreakerRegistry> logger)
    {
        Pipeline = new ResiliencePipelineBuilder
        {
            TimeProvider = timeProvider
        }
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = Math.Clamp(options.FailureRatio, 0.01, 1.0),
            MinimumThroughput = Math.Max(2, options.MinimumThroughput),
            SamplingDuration = TimeSpan.FromSeconds(Math.Max(1, options.SamplingDurationSeconds)),
            BreakDuration = TimeSpan.FromSeconds(Math.Max(1, options.BreakDurationSeconds)),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => !IsBusinessOrClientException(ex)),
            OnOpened = args =>
            {
                var reason = args.Outcome.Exception?.Message ?? "Transient failure threshold exceeded";
                metrics.RecordCircuitOpened(reason, timeProvider.GetUtcNow());
                logger.LogError(
                    args.Outcome.Exception,
                    "Circuit breaker OPENED for {BreakDurationSeconds}s due to downstream failures: {Reason}",
                     args.BreakDuration.TotalSeconds,
                    reason);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                metrics.RecordCircuitClosed(timeProvider.GetUtcNow());
                logger.LogInformation("Circuit breaker CLOSED. Downstream operations recovered.");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = _ =>
            {
                metrics.RecordCircuitHalfOpened(timeProvider.GetUtcNow());
                logger.LogWarning("Circuit breaker HALF-OPENED. Probing downstream health with next request.");
                return ValueTask.CompletedTask;
            }
        })
        .Build();
    }

    private static bool IsBusinessOrClientException(Exception exception) =>
        exception is ValidationException
            or DomainException
            or NotFoundException
            or ConflictException
            or OperationCanceledException;
}

public sealed class CircuitBreakerBehavior<TRequest, TResponse>(
    CircuitBreakerRegistry circuitBreaker,
    ProductMetrics metrics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await circuitBreaker.Pipeline.ExecuteAsync(
                async ct => await next(ct),
                cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            metrics.RecordCircuitBreakerRejected(typeof(TRequest).Name);
            throw;
        }
    }
}
