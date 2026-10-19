using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OrderService.Infrastructure.Messaging;

public sealed class RabbitMqTopologyInitializer
{
    public const string InventoryQueue = "order-service.inventory";
    public const string PaymentQueue = "order-service.payment";
    public const string ReadModelQueue = "order-service.read-model";

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTopologyInitializer> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public RabbitMqTopologyInitializer(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopologyInitializer> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureTopologyAsync(CancellationToken ct = default)
    {
        if (_initialized)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var channel = await _connectionProvider.CreateChannelAsync(publisherConfirms: false, ct);

            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: ct);

            await channel.ExchangeDeclareAsync(
                exchange: _options.DeadLetterExchangeName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: ct);

            await DeclareQueueWithDlqAsync(
                channel,
                InventoryQueue,
                ["inventory.reserved", "inventory.reservation-failed"],
                ct);

            await DeclareQueueWithDlqAsync(
                channel,
                PaymentQueue,
                ["payment.succeeded", "payment.failed"],
                ct);

            await DeclareQueueWithDlqAsync(
                channel,
                ReadModelQueue,
                ["order.*"],
                ct);

            _initialized = true;
            _logger.LogInformation("RabbitMQ topology initialized on exchange {Exchange}", _options.ExchangeName);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DeclareQueueWithDlqAsync(
        IChannel channel,
        string queueName,
        IReadOnlyList<string> routingKeys,
        CancellationToken ct)
    {
        var dlqName = $"{queueName}.dlq";

        await channel.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: dlqName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: dlqName,
            arguments: null,
            cancellationToken: ct);

        var args = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = dlqName
        };

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: ct);

        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                arguments: null,
                cancellationToken: ct);
        }
    }
}
