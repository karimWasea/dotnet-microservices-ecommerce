using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Domain.Orders.Events;

namespace OrderService.Application.Orders.EventHandlers;

public sealed class OrderPlacedDomainEventHandler : INotificationHandler<OrderPlacedDomainEvent>
{
    private readonly ILogger<OrderPlacedDomainEventHandler> _logger;

    public OrderPlacedDomainEventHandler(ILogger<OrderPlacedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderPlacedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Domain event OrderPlaced raised for OrderId={OrderId}, UserId={UserId}, Total={Total} {Currency}, Version={Version}",
            notification.OrderId,
            notification.UserId,
            notification.Total,
            notification.Currency,
            notification.Version);
        return Task.CompletedTask;
    }
}

public sealed class OrderStockReservedDomainEventHandler : INotificationHandler<OrderStockReservedDomainEvent>
{
    private readonly ILogger<OrderStockReservedDomainEventHandler> _logger;

    public OrderStockReservedDomainEventHandler(ILogger<OrderStockReservedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderStockReservedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Domain event OrderStockReserved raised for OrderId={OrderId}, Version={Version}",
            notification.OrderId,
            notification.Version);
        return Task.CompletedTask;
    }
}

public sealed class OrderConfirmedDomainEventHandler : INotificationHandler<OrderConfirmedDomainEvent>
{
    private readonly ILogger<OrderConfirmedDomainEventHandler> _logger;

    public OrderConfirmedDomainEventHandler(ILogger<OrderConfirmedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderConfirmedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Domain event OrderConfirmed raised for OrderId={OrderId}, PaymentId={PaymentId}, Version={Version}",
            notification.OrderId,
            notification.PaymentId,
            notification.Version);
        return Task.CompletedTask;
    }
}

public sealed class OrderRejectedDomainEventHandler : INotificationHandler<OrderRejectedDomainEvent>
{
    private readonly ILogger<OrderRejectedDomainEventHandler> _logger;

    public OrderRejectedDomainEventHandler(ILogger<OrderRejectedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderRejectedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Domain event OrderRejected raised for OrderId={OrderId}, Reason={Reason}, Version={Version}",
            notification.OrderId,
            notification.Reason,
            notification.Version);
        return Task.CompletedTask;
    }
}

public sealed class OrderCancelledDomainEventHandler : INotificationHandler<OrderCancelledDomainEvent>
{
    private readonly ILogger<OrderCancelledDomainEventHandler> _logger;

    public OrderCancelledDomainEventHandler(ILogger<OrderCancelledDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Domain event OrderCancelled raised for OrderId={OrderId}, Reason={Reason}, CancelledBy={CancelledBy}, Version={Version}",
            notification.OrderId,
            notification.Reason,
            notification.CancelledBy,
            notification.Version);
        return Task.CompletedTask;
    }
}
