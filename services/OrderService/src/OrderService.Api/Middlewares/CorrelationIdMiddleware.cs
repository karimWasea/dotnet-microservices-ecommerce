using OrderService.Application.Abstractions;

namespace OrderService.Api.Middlewares;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlationIdAccessor)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString();
        }

        correlationIdAccessor.CorrelationId = correlationId;
        context.Response.Headers.TryAdd(HeaderName, correlationId);

        using (_logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = correlationId }))
        {
            await _next(context);
        }
    }
}
