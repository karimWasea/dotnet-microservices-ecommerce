using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Write;

namespace OrderService.Api.Services;

public sealed class OutboxCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxCleanupService> _logger;

    public OutboxCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupProcessedOutboxMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OutboxCleanupService");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    public async Task<int> CleanupProcessedOutboxMessagesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var cutoff = DateTimeOffset.UtcNow - RetentionPeriod;
        var deleted = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc <= cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {DeletedCount} processed outbox messages older than {Days} days", deleted, RetentionPeriod.TotalDays);
        }

        return deleted;
    }
}
