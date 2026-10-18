namespace ProductService.Api.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!IsValidCorrelationId(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString();
        }

        context.Items[HeaderName] = correlationId!;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId!;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId! }))
        {
            await next(context);
        }
    }

    private static bool IsValidCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var ch in value)
        {
            var isSafe =
                (ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '-' ||
                ch == '_' ||
                ch == '.';

            if (!isSafe)
            {
                return false;
            }
        }

        return true;
    }
}
