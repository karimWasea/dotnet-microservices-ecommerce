using FluentValidation;
using MediatR;
using OrderService.Application.Orders.DTOs;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(
    Guid OrderId,
    Guid RequestingUserId,
    bool IsAdmin,
    string? Reason) : IRequest<CancelOrderResult>;

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty();

        RuleFor(x => x.RequestingUserId)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .MaximumLength(Order.MaxReasonLength);
    }
}

public sealed class CancelOrderHandler : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CancelOrderHandler(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<CancelOrderResult> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return new CancelOrderResult(CancelOrderOutcome.NotFound, "Order was not found.");
        }

        if (!request.IsAdmin && order.UserId != request.RequestingUserId)
        {
            return new CancelOrderResult(CancelOrderOutcome.Forbidden, "You do not have permission to cancel this order.");
        }

        var cancelledBy = request.IsAdmin && order.UserId != request.RequestingUserId
            ? CancelledBy.Admin
            : CancelledBy.Customer;

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Cancelled by user"
            : request.Reason.Trim();

        var transition = order.Cancel(reason, cancelledBy, DateTimeOffset.UtcNow);

        switch (transition)
        {
            case TransitionResult.NoChange:
                return new CancelOrderResult(CancelOrderOutcome.AlreadyCancelled);

            case TransitionResult.NotAllowed:
                return new CancelOrderResult(
                    CancelOrderOutcome.ConflictCannotCancel,
                    $"Order in status '{order.Status}' cannot be cancelled.");

            case TransitionResult.Applied:
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return new CancelOrderResult(CancelOrderOutcome.Cancelled);

            default:
                throw new InvalidOperationException($"Unexpected transition result: {transition}");
        }
    }
}
