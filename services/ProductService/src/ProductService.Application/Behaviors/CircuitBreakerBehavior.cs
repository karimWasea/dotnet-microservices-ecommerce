using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using ProductService.Application.Exceptions;
using ProductService.Application.Monitoring;
using ProductService.Domain.Abstractions;

namespace ProductService.Application.Behaviors;

public sealed class CircuitBreakerRegistry : IDisposable
{
    private readonly SemaphoreSlim _executionSlots;
    private readonly SemaphoreSlim _queueSlots;
    private readonly ProductMetrics _metrics;
    private readonly ILogger<CircuitBreakerRegistry> _logger;

    public ResiliencePipeline Pipeline { get; }

    public CircuitBreakerRegistry(
        CircuitBreakerOptions options,
        ProductMetrics metrics,
        TimeProvider timeProvider,
        ILogger<CircuitBreakerRegistry> logger)
    {
        _metrics = metrics;
        _logger = logger;

        var maxParallel = Math.Max(1, options.BulkheadMaxParallelization);
        var maxQueue = Math.Max(0, options.BulkheadMaxQueuedActions);

        _executionSlots = new SemaphoreSlim(maxParallel, maxParallel);
        // Total admission permits = active execution slots + waiting queue slots
        _queueSlots = new SemaphoreSlim(maxParallel + maxQueue, maxParallel + maxQueue);

        var builder = new ResiliencePipelineBuilder
        {
            TimeProvider = timeProvider
        };

        // 1. Middle layer: Retry with exponential backoff & jitter (only when MaxRetryAttempts > 0)
        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(Math.Max(1, options.RetryBaseDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = options.RetryUseJitter,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                    !IsBusinessOrClientException(ex) && ex is not BrokenCircuitException and not BulkheadRejectedException),
                OnRetry = args =>
                {
                    var reason = args.Outcome.Exception?.Message ?? "Transient error";
                    metrics.RecordRetryAttempt(args.AttemptNumber + 1, reason);
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Retry attempt {AttemptNumber} after {RetryDelayMs}ms due to transient failure: {Reason}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        reason);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // 2. Inner layer: Circuit Breaker (counts each failed attempt and trips Open when threshold is reached)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
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
        });

        Pipeline = builder.Build();
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        string requestName,
        Func<CancellationToken, Task<TResponse>> action,
        CancellationToken cancellationToken)
    {
        // Outer layer: Bulkhead isolation (non-blocking check for total admission capacity = active + queue)
        if (!await _queueSlots.WaitAsync(0, cancellationToken))
        {
            _metrics.RecordBulkheadRejected(requestName);
            _logger.LogWarning(
                "Bulkhead rejected request {RequestName}: maximum concurrency and queue capacity reached.",
                requestName);
            throw new BulkheadRejectedException(
                "The server bulkhead capacity is full. Too many concurrent requests are in flight.");
        }

        _metrics.IncrementBulkheadQueued();
        var acquiredExecutionSlot = false;
        try
        {
            await _executionSlots.WaitAsync(cancellationToken);
            acquiredExecutionSlot = true;
            _metrics.DecrementBulkheadQueued();
            _metrics.IncrementBulkheadActive();

            return await Pipeline.ExecuteAsync(
                async ct => await action(ct),
                cancellationToken);
        }
        finally
        {
            if (acquiredExecutionSlot)
            {
                _metrics.DecrementBulkheadActive();
                _executionSlots.Release();
            }
            else
            {
                _metrics.DecrementBulkheadQueued();
            }

            _queueSlots.Release();
        }
    }

    private static bool IsBusinessOrClientException(Exception exception) =>
        exception is ValidationException
            or DomainException
            or NotFoundException
            or ConflictException
            or BulkheadRejectedException
            or OperationCanceledException;

    public void Dispose()
    {
        _executionSlots.Dispose();
        _queueSlots.Dispose();
    }
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
        var requestName = typeof(TRequest).Name;

        try
        {
            return await circuitBreaker.ExecuteAsync(
                requestName,
                async ct => await next(ct),
                cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            metrics.RecordCircuitBreakerRejected(requestName);
            throw;
        }
    }
}
