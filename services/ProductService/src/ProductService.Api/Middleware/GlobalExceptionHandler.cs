using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;

namespace ProductService.Api.Middleware;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request {Method} {Path} was canceled by the client.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var (statusCode, title, type) = exception switch
        {
            ValidationException or BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                "Validation Failed",
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"),
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Not Found",
                "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
            ConflictException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
            DomainException => (
                StatusCodes.Status422UnprocessableEntity,
                "Domain Rule Violation",
                "https://tools.ietf.org/html/rfc9110#section-15.5.21"),
            BulkheadRejectedException => (
                StatusCodes.Status429TooManyRequests,
                "Too Many Concurrent Requests",
                "https://tools.ietf.org/html/rfc6585#section-4"),
            BrokenCircuitException => (
                StatusCodes.Status503ServiceUnavailable,
                "Service Temporarily Unavailable",
                "https://tools.ietf.org/html/rfc9110#section-15.6.4"),
            TimeoutException or OperationCanceledException => (
                StatusCodes.Status504GatewayTimeout,
                "Request Timeout",
                "https://tools.ietf.org/html/rfc9110#section-15.6.5"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "Request {Method} {Path} failed with status {StatusCode}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode,
                exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;
        if (exception is BrokenCircuitException)
        {
            httpContext.Response.Headers.RetryAfter = "15";
        }
        else if (exception is BulkheadRejectedException)
        {
            httpContext.Response.Headers.RetryAfter = "5";
        }

        ProblemDetails problemDetails;

        if (exception is ValidationException validationException)
        {
            var errors = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            problemDetails = new HttpValidationProblemDetails(errors)
            {
                Status = statusCode,
                Title = title,
                Type = type,
                Detail = "One or more validation errors occurred.",
                Instance = httpContext.Request.Path
            };
        }
        else
        {
            var detail = exception switch
            {
                BrokenCircuitException =>
                    "The service is temporarily unavailable because the circuit breaker is open. Please retry shortly.",
                _ when statusCode == StatusCodes.Status500InternalServerError =>
                    "An unexpected error occurred.",
                _ => exception.Message
            };

            problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Type = type,
                Detail = detail,
                Instance = httpContext.Request.Path
            };

            if (exception is DomainException domainException)
            {
                problemDetails.Extensions["code"] = domainException.Code;
            }
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
