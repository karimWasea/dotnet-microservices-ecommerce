using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Infrastructure.Write;
using OrderService.Infrastructure.Write.Models;
using RabbitMQ.Client;

namespace OrderService.Infrastructure.Messaging;

public sealed class OutboxPublisherService : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdlePollDelay = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqTopologyInitializer _topologyInitializer;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqTopologyInitializer topologyInitializer,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionProvider = connectionProvider;
        _topologyInitializer = topologyInitializer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("RabbitMQ is disabled; OutboxPublisherService will not run.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publishedCount = await PublishBatchAsync(stoppingToken);
                if (publishedCount < BatchSize)
                {
                    await Task.Delay(IdlePollDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in OutboxPublisherService loop; retrying in 2s.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    public async Task<int> PublishBatchAsync(CancellationToken ct = default)
    {
        await _topologyInitializer.EnsureTopologyAsync(ct);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var messages = await dbContext.OutboxMessages
            .FromSqlInterpolated($@"
                SELECT *
                FROM outbox_messages
                WHERE processed_on_utc IS NULL
                  AND attempts < {OutboxMessage.MaxAttempts}
                  AND (next_retry_at_utc IS NULL OR next_retry_at_utc <= {now})
                ORDER BY occurred_on_utc
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return 0;
        }

        await using var channel = await _connectionProvider.CreateChannelAsync(publisherConfirms: true, ct);

        foreach (var msg in messages)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(msg.Payload);
                var props = new BasicProperties
                {
                    DeliveryMode = DeliveryModes.Persistent,
                    ContentType = "application/json",
                    MessageId = msg.Id.ToString(),
                    Type = msg.Type,
                    Timestamp = new AmqpTimestamp(msg.OccurredOnUtc.ToUnixTimeSeconds()),
                    CorrelationId = msg.CorrelationId ?? msg.Id.ToString(),
                    Headers = new Dictionary<string, object?>
                    {
                        ["x-schema-version"] = 1,
                        ["x-source"] = "order-service"
                    }
                };

                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: msg.Type,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: ct);

                msg.ProcessedOnUtc = DateTimeOffset.UtcNow;
                msg.LastError = null;
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                msg.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                var backoffSeconds = Math.Min(300, Math.Pow(2, msg.Attempts));
                msg.NextRetryAtUtc = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);

                if (msg.Attempts >= OutboxMessage.MaxAttempts)
                {
                    _logger.LogCritical(
                        ex,
                        "Outbox message {OutboxMessageId} (Type={Type}) reached max attempts ({MaxAttempts})",
                        msg.Id,
                        msg.Type,
                        OutboxMessage.MaxAttempts);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to publish outbox message {OutboxMessageId} (Attempt={Attempt}); next retry at {NextRetryAtUtc}",
                        msg.Id,
                        msg.Attempts,
                        msg.NextRetryAtUtc);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return messages.Count;
    }
}
