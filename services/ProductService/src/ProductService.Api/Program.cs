using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductService.Api.Endpoints;
using ProductService.Api.Middleware;
using ProductService.Application;
using ProductService.Application.Monitoring;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;
using ProductService.Infrastructure;
using ProductService.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication(options =>
    builder.Configuration.GetSection("CircuitBreaker").Bind(options));
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var defaultTimeoutMs = builder.Configuration.GetValue("RequestTimeouts:DefaultMs", 10_000);
var readTimeoutMs = builder.Configuration.GetValue("RequestTimeouts:ReadMs", 5_000);
var writeTimeoutMs = builder.Configuration.GetValue("RequestTimeouts:WriteMs", 10_000);

static RequestTimeoutPolicy CreateTimeoutPolicy(int timeoutMs) => new()
{
    Timeout = TimeSpan.FromMilliseconds(timeoutMs),
    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
    WriteTimeoutResponse = async context =>
    {
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Request Timeout",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.5",
                Detail = "The request timed out before completion.",
                Instance = context.Request.Path
            }
        });
    }
};

builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = CreateTimeoutPolicy(defaultTimeoutMs);
    options.AddPolicy(ProductEndpoints.ReadTimeoutPolicy, CreateTimeoutPolicy(readTimeoutMs));
    options.AddPolicy(ProductEndpoints.WriteTimeoutPolicy, CreateTimeoutPolicy(writeTimeoutMs));
});

builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<ProductDbContext>(
        name: "productdb",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"])
    .AddCheck<CircuitBreakerHealthCheck>(
        name: "circuit-breaker",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    var seedSampleData = app.Configuration.GetValue("Database:SeedSampleData", true);

    try
    {
        await ProductDbSeeder.MigrateAndSeedAsync(dbContext, repository, unitOfWork, timeProvider, seedSampleData);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(
            ex,
            "Database migration or seeding could not be completed at startup. Readiness probe (/health/ready) will report status.");
    }
}

app.MapProductEndpoints();

app.MapGet("/metrics", (ProductMetrics metrics) =>
    Results.Text(metrics.FormatPrometheusMetrics(), "text/plain; version=0.0.4; charset=utf-8"))
    .WithTags("Monitoring")
    .WithName("GetPrometheusMetrics")
    .WithSummary("Prometheus metrics scrape endpoint");

app.MapGet("/monitoring/status", (ProductMetrics metrics) =>
    TypedResults.Ok(metrics.GetSnapshot()))
    .WithTags("Monitoring")
    .WithName("GetMonitoringStatus")
    .WithSummary("Live monitoring snapshot including circuit breaker state and request counters");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).WithTags("Health");

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).WithTags("Health");

app.Run();

public sealed class CircuitBreakerHealthCheck(ProductMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(metrics.CircuitState switch
        {
            "Open" => HealthCheckResult.Unhealthy("Circuit breaker is OPEN."),
            "HalfOpen" => HealthCheckResult.Degraded("Circuit breaker is HALF-OPEN."),
            _ => HealthCheckResult.Healthy("Circuit breaker is CLOSED.")
        });
}

public partial class Program;


