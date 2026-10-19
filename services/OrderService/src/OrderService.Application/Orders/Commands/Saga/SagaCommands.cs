using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.Saga;

public sealed record RecordStockReservedCommand(
    Guid MessageId,
    string ConsumerName,
    Guid OrderId,
    DateTimeOffset ReservedAtUtc) : IRequest<TransitionResult>, IInboxMessage;

public sealed class RecordStockReservedHandler : IRequestHandler<RecordStockReservedCommand, TransitionResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<RecordStockReservedHandler> _logger;

    public RecordStockReservedHandler(
        IOrderRepository orderRepository,
        ILogger<RecordStockReservedHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<TransitionResult> Handle(RecordStockReservedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found when handling StockReserved (MessageId={MessageId})", request.OrderId, request.MessageId);
            return TransitionResult.NoChange;
        }

        var result = order.MarkStockReserved(request.ReservedAtUtc);
        if (result == TransitionResult.NotAllowed)
        {
            _logger.LogWarning(
                "Ignored StockReserved for order {OrderId} in status {Status} (MessageId={MessageId})",
                order.Id,
                order.Status,
                request.MessageId);
        }

        return result;
    }
}

public sealed record RecordStockReservationFailedCommand(
    Guid MessageId,
    string ConsumerName,
    Guid OrderId,
    string Reason,
    DateTimeOffset FailedAtUtc) : IRequest<TransitionResult>, IInboxMessage;

public sealed class RecordStockReservationFailedHandler : IRequestHandler<RecordStockReservationFailedCommand, TransitionResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<RecordStockReservationFailedHandler> _logger;

    public RecordStockReservationFailedHandler(
        IOrderRepository orderRepository,
        ILogger<RecordStockReservationFailedHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<TransitionResult> Handle(RecordStockReservationFailedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found when handling StockReservationFailed (MessageId={MessageId})", request.OrderId, request.MessageId);
            return TransitionResult.NoChange;
        }

        var result = order.Reject(request.Reason, request.FailedAtUtc);
        if (result == TransitionResult.NotAllowed)
        {
            _logger.LogWarning(
                "Ignored StockReservationFailed for order {OrderId} in status {Status} (MessageId={MessageId})",
                order.Id,
                order.Status,
                request.MessageId);
        }

        return result;
    }
}

public sealed record RecordPaymentSucceededCommand(
    Guid MessageId,
    string ConsumerName,
    Guid OrderId,
    string PaymentId,
    DateTimeOffset PaidAtUtc) : IRequest<TransitionResult>, IInboxMessage;

public sealed class RecordPaymentSucceededHandler : IRequestHandler<RecordPaymentSucceededCommand, TransitionResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<RecordPaymentSucceededHandler> _logger;

    public RecordPaymentSucceededHandler(
        IOrderRepository orderRepository,
        ILogger<RecordPaymentSucceededHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<TransitionResult> Handle(RecordPaymentSucceededCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found when handling PaymentSucceeded (MessageId={MessageId})", request.OrderId, request.MessageId);
            return TransitionResult.NoChange;
        }

        var result = order.Confirm(request.PaymentId, request.PaidAtUtc);
        if (result == TransitionResult.NotAllowed)
        {
            _logger.LogWarning(
                "Ignored PaymentSucceeded for order {OrderId} in status {Status} (MessageId={MessageId})",
                order.Id,
                order.Status,
                request.MessageId);
        }

        return result;
    }
}

public sealed record RecordPaymentFailedCommand(
    Guid MessageId,
    string ConsumerName,
    Guid OrderId,
    string Reason,
    DateTimeOffset FailedAtUtc) : IRequest<TransitionResult>, IInboxMessage;

public sealed class RecordPaymentFailedHandler : IRequestHandler<RecordPaymentFailedCommand, TransitionResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<RecordPaymentFailedHandler> _logger;

    public RecordPaymentFailedHandler(
        IOrderRepository orderRepository,
        ILogger<RecordPaymentFailedHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<TransitionResult> Handle(RecordPaymentFailedCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found when handling PaymentFailed (MessageId={MessageId})", request.OrderId, request.MessageId);
            return TransitionResult.NoChange;
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "PaymentFailed"
            : request.Reason.Trim();

        var result = order.Cancel(reason, CancelledBy.System, request.FailedAtUtc);
        if (result == TransitionResult.NotAllowed)
        {
            _logger.LogWarning(
                "Ignored PaymentFailed for order {OrderId} in status {Status} (MessageId={MessageId})",
                order.Id,
                order.Status,
                request.MessageId);
        }

        return result;
    }
}
