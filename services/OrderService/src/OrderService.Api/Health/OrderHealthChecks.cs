using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderService.Infrastructure.Read;
using OrderService.Infrastructure.Write;

namespace OrderService.Api.Health;

public sealed class OrderWriteDbHealthCheck : IHealthCheck
{
    private readonly OrderDbContext _dbContext;

    public OrderWriteDbHealthCheck(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Write database (orderdb) reachable.")
                : HealthCheckResult.Unhealthy("Cannot connect to write database (orderdb).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Exception connecting to write database (orderdb).", ex);
        }
    }
}

public sealed class OrderReadDbHealthCheck : IHealthCheck
{
    private readonly OrderReadDbContext _readDbContext;

    public OrderReadDbHealthCheck(OrderReadDbContext readDbContext)
    {
        _readDbContext = readDbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _readDbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Read database (orderreaddb) reachable.")
                : HealthCheckResult.Unhealthy("Cannot connect to read database (orderreaddb).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Exception connecting to read database (orderreaddb).", ex);
        }
    }
}

public sealed class OutboxLagHealthCheck : IHealthCheck
{
    private const int MaxLagThreshold = 1000;
    private static readonly TimeSpan StaleLagThreshold = TimeSpan.FromMinutes(5);

    private readonly OrderDbContext _dbContext;

    public OutboxLagHealthCheck(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _dbContext.OutboxMessages
                .AsNoTracking()
                .CountAsync(m => m.ProcessedOnUtc == null, cancellationToken);

            var oldestUnprocessed = await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(m => m.ProcessedOnUtc == null)
                .OrderBy(m => m.OccurredOnUtc)
                .Select(m => (DateTimeOffset?)m.OccurredOnUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["unprocessed_count"] = count,
                ["oldest_unprocessed_utc"] = oldestUnprocessed?.ToString("O") ?? "none"
            };

            if (count > MaxLagThreshold)
            {
                return HealthCheckResult.Degraded($"Outbox lag exceeds {MaxLagThreshold} messages ({count}).", data: data);
            }

            if (oldestUnprocessed.HasValue &&
                DateTimeOffset.UtcNow - oldestUnprocessed.Value > StaleLagThreshold)
            {
                return HealthCheckResult.Degraded($"Oldest unprocessed outbox message is older than {StaleLagThreshold.TotalMinutes} minutes.", data: data);
            }

            return HealthCheckResult.Healthy($"Outbox lag is healthy ({count} unprocessed).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to query outbox lag.", ex);
        }
    }
}
