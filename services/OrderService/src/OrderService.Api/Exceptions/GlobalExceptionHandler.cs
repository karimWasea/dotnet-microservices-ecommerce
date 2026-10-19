using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Exceptions;
using OrderService.Domain.Abstractions;

namespace OrderService.Api.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = MapException(httpContext, exception);

        _logger.LogError(
            exception,
            "Handled exception of type {ExceptionType} with status code {StatusCode}: {Message}",
            exception.GetType().Name,
            statusCode,
            exception.Message);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static (int StatusCode, ProblemDetails ProblemDetails) MapException(
        HttpContext httpContext,
        Exception exception)
    {
        var traceId = httpContext.TraceIdentifier;
        var instance = httpContext.Request.Path.Value;

        switch (exception)
        {
            case ValidationException valEx:
            {
                var errors = valEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Validation Failed",
                    Detail = "One or more validation errors occurred.",
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = "validation.failed",
                        ["errors"] = errors
                    }
                };
                return (StatusCodes.Status400BadRequest, pd);
            }

            case ProductNotAvailableException prodEx:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Type = "https://tools.ietf.org/html/rfc4918#section-11.2",
                    Title = "Product Unavailable",
                    Detail = prodEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = prodEx.Code,
                        ["product_id"] = prodEx.ProductId
                    }
                };
                return (StatusCodes.Status422UnprocessableEntity, pd);
            }

            case NotFoundException notFoundEx:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    Title = "Not Found",
                    Detail = notFoundEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = notFoundEx.Code
                    }
                };
                return (StatusCodes.Status404NotFound, pd);
            }

            case ConflictException conflictEx:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                    Title = "Conflict",
                    Detail = conflictEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = conflictEx.Code
                    }
                };
                return (StatusCodes.Status409Conflict, pd);
            }

            case DependencyUnavailableException depEx:
            {
                if (depEx.RetryAfterSeconds > 0)
                {
                    httpContext.Response.Headers.TryAdd("Retry-After", depEx.RetryAfterSeconds.ToString());
                }

                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                    Title = "Dependency Unavailable",
                    Detail = depEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = depEx.Code,
                        ["dependency"] = depEx.DependencyName,
                        ["retry_after_seconds"] = depEx.RetryAfterSeconds
                    }
                };
                return (StatusCodes.Status503ServiceUnavailable, pd);
            }

            case DependencyTimeoutException timeoutEx:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status504GatewayTimeout,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.5",
                    Title = "Gateway Timeout",
                    Detail = timeoutEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = timeoutEx.Code,
                        ["dependency"] = timeoutEx.DependencyName
                    }
                };
                return (StatusCodes.Status504GatewayTimeout, pd);
            }

            case DomainException domEx:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Domain Rule Violation",
                    Detail = domEx.Message,
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = domEx.Code
                    }
                };
                return (StatusCodes.Status400BadRequest, pd);
            }

            default:
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                    Title = "Internal Server Error",
                    Detail = "An unexpected error occurred. Please try again later.",
                    Instance = instance,
                    Extensions =
                    {
                        ["trace_id"] = traceId,
                        ["code"] = "internal_error"
                    }
                };
                return (StatusCodes.Status500InternalServerError, pd);
            }
        }
    }
}
