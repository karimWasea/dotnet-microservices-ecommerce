using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Application.Abstractions;
using OrderService.Application.IntegrationEvents.Incoming;
using OrderService.Application.IntegrationEvents.Outgoing;
using OrderService.Application.Orders.Commands.Saga;
using OrderService.Infrastructure.Read.Projections;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Infrastructure.Messaging.Consumers;

public abstract class RabbitMqConsumerBase : BackgroundService
{
    private const ushort PrefetchCount = 10;
    private static readonly int[] RetryDelaysSeconds = [1, 2, 4, 8];
    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqTopologyInitializer _topologyInitializer;
    private readonly RabbitMqOptions _options;
    private readonly ILogger _logger;

    protected abstract string QueueName { get; }
    protected abstract string ConsumerName { get; }

    protected RabbitMqConsumerBase(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqTopologyInitializer topologyInitializer,
        IOptions<RabbitMqOptions> options,
        ILogger logger)
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
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                await _topologyInitializer.EnsureTopologyAsync(stoppingToken);
                channel = await _connectionProvider.CreateChannelAsync(publisherConfirms: false, stoppingToken);
                await channel.BasicQosAsync(0, PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    await ProcessDeliveryAsync(channel, ea, stoppingToken);
                };

                await channel.BasicConsumeAsync(
                    queue: QueueName,
                    autoAck: false,
                    consumerTag: $"{ConsumerName}-{Guid.NewGuid():N}",
                    noLocal: false,
                    exclusive: false,
                    arguments: null,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Started RabbitMQ consumer {ConsumerName} on queue {QueueName}", ConsumerName, QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Consumer {ConsumerName} encountered channel error; reconnecting in 3s.", ConsumerName);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            finally
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }
            }
        }
    }

    private async Task ProcessDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var bodyBytes = ea.Body.ToArray();
        var bodyJson = Encoding.UTF8.GetString(bodyBytes);
        var messageId = ExtractMessageId(ea.BasicProperties?.MessageId, bodyBytes);
        var correlationId = ea.BasicProperties?.CorrelationId ?? messageId.ToString();
        var routingKey = ea.RoutingKey;

        for (var attempt = 1; attempt <= RetryDelaysSeconds.Length + 1; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(HandlerTimeout);

                using var scope = _scopeFactory.CreateScope();
                var correlationAccessor = scope.ServiceProvider.GetService<ICorrelationIdAccessor>();
                if (correlationAccessor is not null)
                {
                    correlationAccessor.CorrelationId = correlationId;
                }

                await HandleMessageAsync(scope.ServiceProvider, messageId, routingKey, bodyJson, cts.Token);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                return;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(
                    jsonEx,
                    "Poison message (invalid JSON) on queue {QueueName} (MessageId={MessageId}, RoutingKey={RoutingKey}); sending immediately to DLQ.",
                    QueueName,
                    messageId,
                    routingKey);

                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                if (attempt > RetryDelaysSeconds.Length)
                {
                    _logger.LogError(
                        ex,
                        "Message {MessageId} (RoutingKey={RoutingKey}) failed after {MaxAttempts} attempts in {ConsumerName}; sending to DLQ.",
                        messageId,
                        routingKey,
                        attempt,
                        ConsumerName);

                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                    return;
                }

                var delaySeconds = RetryDelaysSeconds[attempt - 1];
                _logger.LogWarning(
                    ex,
                    "Transient failure handling MessageId={MessageId} (Attempt {Attempt}/{MaxAttempts}) in {ConsumerName}; retrying in {DelaySeconds}s.",
                    messageId,
                    attempt,
                    RetryDelaysSeconds.Length + 1,
                    ConsumerName,
                    delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }
    }

    protected abstract Task HandleMessageAsync(
        IServiceProvider serviceProvider,
        Guid messageId,
        string routingKey,
        string bodyJson,
        CancellationToken ct);

    private static Guid ExtractMessageId(string? rawMessageId, byte[] bodyBytes)
    {
        if (Guid.TryParse(rawMessageId, out var parsed))
        {
            return parsed;
        }

        var hash = SHA256.HashData(bodyBytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class InventoryEventsConsumer : RabbitMqConsumerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override string QueueName => RabbitMqTopologyInitializer.InventoryQueue;
    protected override string ConsumerName => nameof(InventoryEventsConsumer);

    public InventoryEventsConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqTopologyInitializer topologyInitializer,
        IOptions<RabbitMqOptions> options,
        ILogger<InventoryEventsConsumer> logger)
        : base(scopeFactory, connectionProvider, topologyInitializer, options, logger)
    {
    }

    protected override async Task HandleMessageAsync(
        IServiceProvider serviceProvider,
        Guid messageId,
        string routingKey,
        string bodyJson,
        CancellationToken ct)
    {
        var sender = serviceProvider.GetRequiredService<ISender>();

        switch (routingKey)
        {
            case StockReservedIntegrationEvent.RoutingKey:
            case "inventory.stock-reserved":
            {
                var evt = JsonSerializer.Deserialize<StockReservedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized StockReservedIntegrationEvent was null.");

                await sender.Send(
                    new RecordStockReservedCommand(messageId, ConsumerName, evt.OrderId, evt.OccurredAtUtc),
                    ct);
                break;
            }

            case StockReservationFailedIntegrationEvent.RoutingKey:
            case "inventory.stock-reservation-failed":
            {
                var evt = JsonSerializer.Deserialize<StockReservationFailedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized StockReservationFailedIntegrationEvent was null.");

                await sender.Send(
                    new RecordStockReservationFailedCommand(messageId, ConsumerName, evt.OrderId, evt.Reason, evt.OccurredAtUtc),
                    ct);
                break;
            }
        }
    }
}

public sealed class PaymentEventsConsumer : RabbitMqConsumerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override string QueueName => RabbitMqTopologyInitializer.PaymentQueue;
    protected override string ConsumerName => nameof(PaymentEventsConsumer);

    public PaymentEventsConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqTopologyInitializer topologyInitializer,
        IOptions<RabbitMqOptions> options,
        ILogger<PaymentEventsConsumer> logger)
        : base(scopeFactory, connectionProvider, topologyInitializer, options, logger)
    {
    }

    protected override async Task HandleMessageAsync(
        IServiceProvider serviceProvider,
        Guid messageId,
        string routingKey,
        string bodyJson,
        CancellationToken ct)
    {
        var sender = serviceProvider.GetRequiredService<ISender>();

        switch (routingKey)
        {
            case PaymentSucceededIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<PaymentSucceededIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized PaymentSucceededIntegrationEvent was null.");

                await sender.Send(
                    new RecordPaymentSucceededCommand(messageId, ConsumerName, evt.OrderId, evt.PaymentId, evt.OccurredAtUtc),
                    ct);
                break;
            }

            case PaymentFailedIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<PaymentFailedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized PaymentFailedIntegrationEvent was null.");

                await sender.Send(
                    new RecordPaymentFailedCommand(messageId, ConsumerName, evt.OrderId, evt.Reason, evt.OccurredAtUtc),
                    ct);
                break;
            }
        }
    }
}

public sealed class OrderReadModelConsumer : RabbitMqConsumerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override string QueueName => RabbitMqTopologyInitializer.ReadModelQueue;
    protected override string ConsumerName => nameof(OrderReadModelConsumer);

    public OrderReadModelConsumer(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqTopologyInitializer topologyInitializer,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderReadModelConsumer> logger)
        : base(scopeFactory, connectionProvider, topologyInitializer, options, logger)
    {
    }

    protected override async Task HandleMessageAsync(
        IServiceProvider serviceProvider,
        Guid messageId,
        string routingKey,
        string bodyJson,
        CancellationToken ct)
    {
        var projector = serviceProvider.GetRequiredService<OrderReadModelProjector>();

        switch (routingKey)
        {
            case OrderPlacedIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderPlacedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized OrderPlacedIntegrationEvent was null.");
                await projector.ProjectOrderPlacedAsync(evt, ct);
                break;
            }

            case OrderStockReservedIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderStockReservedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized OrderStockReservedIntegrationEvent was null.");
                await projector.ProjectOrderStockReservedAsync(evt, ct);
                break;
            }

            case OrderConfirmedIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderConfirmedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized OrderConfirmedIntegrationEvent was null.");
                await projector.ProjectOrderConfirmedAsync(evt, ct);
                break;
            }

            case OrderRejectedIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderRejectedIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized OrderRejectedIntegrationEvent was null.");
                await projector.ProjectOrderRejectedAsync(evt, ct);
                break;
            }

            case OrderCancelledIntegrationEvent.RoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(bodyJson, JsonOptions)
                    ?? throw new JsonException("Deserialized OrderCancelledIntegrationEvent was null.");
                await projector.ProjectOrderCancelledAsync(evt, ct);
                break;
            }
        }
    }
}
