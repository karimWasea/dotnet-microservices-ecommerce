using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FulfillmentSimulator;

public sealed class SimulatorWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SimulatorOptions _options;
    private readonly ILogger<SimulatorWorker> _logger;

    public SimulatorWorker(
        IOptions<SimulatorOptions> options,
        ILogger<SimulatorWorker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.Host,
                    Port = _options.Port,
                    VirtualHost = _options.VirtualHost,
                    UserName = _options.Username,
                    Password = _options.Password,
                    AutomaticRecoveryEnabled = true
                };

                await using var connection = await factory.CreateConnectionAsync("fulfillment-simulator", stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                await channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                await channel.ExchangeDeclareAsync(
                    exchange: _options.DeadLetterExchangeName,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                var dlqName = $"{_options.QueueName}.dlq";
                await channel.QueueDeclareAsync(
                    queue: dlqName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: dlqName,
                    exchange: _options.DeadLetterExchangeName,
                    routingKey: dlqName,
                    arguments: null,
                    cancellationToken: stoppingToken);

                var queueArgs = new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                    ["x-dead-letter-routing-key"] = dlqName
                };

                await channel.QueueDeclareAsync(
                    queue: _options.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.QueueName,
                    exchange: _options.ExchangeName,
                    routingKey: "order.placed",
                    arguments: null,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.QueueName,
                    exchange: _options.ExchangeName,
                    routingKey: "order.cancelled",
                    arguments: null,
                    cancellationToken: stoppingToken);

                await channel.BasicQosAsync(0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    try
                    {
                        var bodyBytes = ea.Body.ToArray();
                        var bodyJson = Encoding.UTF8.GetString(bodyBytes);
                        var correlationId = ea.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString();

                        switch (ea.RoutingKey)
                        {
                            case "order.placed":
                                await HandleOrderPlacedAsync(channel, bodyJson, correlationId, stoppingToken);
                                break;

                            case "order.cancelled":
                                HandleOrderCancelled(bodyJson);
                                break;
                        }

                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message in SimulatorWorker");
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: _options.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("FulfillmentSimulator listening on queue {QueueName}", _options.QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FulfillmentSimulator connection error; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleOrderPlacedAsync(
        IChannel channel,
        string bodyJson,
        string correlationId,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(bodyJson);
        var root = doc.RootElement;

        var orderId = root.GetProperty("orderId").GetGuid();
        var total = root.GetProperty("total").GetDecimal();
        var currency = root.GetProperty("currency").GetString() ?? "USD";

        var failInventory = false;
        var failPayment = false;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("productName", out var name) &&
                    name.GetString()?.Contains("fail-inventory", StringComparison.OrdinalIgnoreCase) == true)
                {
                    failInventory = true;
                }

                if (item.TryGetProperty("productName", out var namePay) &&
                    namePay.GetString()?.Contains("fail-payment", StringComparison.OrdinalIgnoreCase) == true)
                {
                    failPayment = true;
                }

                if (item.TryGetProperty("quantity", out var qty) && qty.GetInt32() == 99)
                {
                    failInventory = true;
                }
            }
        }

        await Task.Delay(_options.StepDelayMs, ct);

        if (failInventory)
        {
            _logger.LogWarning("Simulating stock reservation failure for OrderId={OrderId}", orderId);
            var failPayload = JsonSerializer.Serialize(new
            {
                orderId,
                reason = "Simulated insufficient stock for product.",
                occurredAtUtc = DateTimeOffset.UtcNow
            }, JsonOptions);

            await PublishEventAsync(channel, "inventory.reservation-failed", failPayload, correlationId, ct);
            return;
        }

        _logger.LogInformation("Simulating stock reserved for OrderId={OrderId}", orderId);
        var successPayload = JsonSerializer.Serialize(new
        {
            orderId,
            occurredAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

        await PublishEventAsync(channel, "inventory.reserved", successPayload, correlationId, ct);

        await Task.Delay(_options.StepDelayMs, ct);

        if (failPayment)
        {
            _logger.LogWarning("Simulating payment capture failure for OrderId={OrderId}", orderId);
            var paymentFailPayload = JsonSerializer.Serialize(new
            {
                orderId,
                reason = "Simulated card limit exceeded.",
                occurredAtUtc = DateTimeOffset.UtcNow
            }, JsonOptions);

            await PublishEventAsync(channel, "payment.failed", paymentFailPayload, correlationId, ct);
            return;
        }

        _logger.LogInformation("Simulating payment captured for OrderId={OrderId}, Total={Total} {Currency}", orderId, total, currency);
        var paymentSuccessPayload = JsonSerializer.Serialize(new
        {
            orderId,
            paymentId = $"pay_sim_{Guid.NewGuid():N}"[..18],
            amount = total,
            currency,
            occurredAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

        await PublishEventAsync(channel, "payment.succeeded", paymentSuccessPayload, correlationId, ct);
    }

    private void HandleOrderCancelled(string bodyJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            var orderId = doc.RootElement.GetProperty("orderId").GetGuid();
            var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : "Cancelled";
            _logger.LogInformation("Simulated compensation executed: released inventory for cancelled order {OrderId} (Reason='{Reason}')", orderId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse order.cancelled payload");
        }
    }

    private async Task PublishEventAsync(
        IChannel channel,
        string routingKey,
        string payloadJson,
        string correlationId,
        CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(payloadJson);
        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            MessageId = Guid.CreateVersion7().ToString(),
            Type = routingKey,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            CorrelationId = correlationId,
            Headers = new Dictionary<string, object?>
            {
                ["x-schema-version"] = 1,
                ["x-source"] = "fulfillment-simulator"
            }
        };

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }
}
