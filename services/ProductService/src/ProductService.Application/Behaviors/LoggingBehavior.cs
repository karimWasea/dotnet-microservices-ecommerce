using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using ProductService.Application.Monitoring;

namespace ProductService.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ProductMetrics? metrics = null)
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
        using var activity = metrics?.ActivitySource.StartActivity(requestName);
        activity?.SetTag("mediatr.request", requestName);

        logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            metrics?.RecordRequestCompleted(requestName, stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            metrics?.RecordRequestCanceled(requestName, stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, "Canceled");

            logger.LogInformation(
                "Request {RequestName} was canceled after {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            metrics?.RecordRequestFailed(requestName, stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            logger.LogWarning(
                "Request {RequestName} failed after {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}


