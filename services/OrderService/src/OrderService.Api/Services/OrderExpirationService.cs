using MediatR;
using OrderService.Application.Orders.Commands.ExpireOrder;
using OrderService.Domain.Orders;

namespace OrderService.Api.Services;

public sealed class OrderExpirationOptions
{
    public const string SectionName = "OrderExpiration";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 30;
    public int StaleThresholdMinutes { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
}

public sealed class OrderExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrderExpirationOptions _options;
    private readonly ILogger<OrderExpirationService> _logger;

    public OrderExpirationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OrderExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = new OrderExpirationOptions();
        configuration.GetSection(OrderExpirationOptions.SectionName).Bind(_options);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("OrderExpirationService is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireStaleOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OrderExpirationService");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
        }
    }

    public async Task<int> ExpireStaleOrdersAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.StaleThresholdMinutes);
        var staleOrderIds = await repository.GetStaleOrderIdsAsync(cutoff, _options.BatchSize, ct);

        var expiredCount = 0;
        foreach (var orderId in staleOrderIds)
        {
            try
            {
                var expired = await sender.Send(new ExpireOrderCommand(orderId), ct);
                if (expired)
                {
                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to expire stale order {OrderId}", orderId);
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Expired {ExpiredCount} stale orders older than {StaleMinutes} minutes", expiredCount, _options.StaleThresholdMinutes);
        }

        return expiredCount;
    }
}
