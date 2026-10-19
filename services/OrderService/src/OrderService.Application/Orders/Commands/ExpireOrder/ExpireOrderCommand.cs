using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.ExpireOrder;

public sealed record ExpireOrderCommand(
    Guid OrderId,
    string Reason = "OrderExpired") : IRequest<bool>;

public sealed class ExpireOrderHandler : IRequestHandler<ExpireOrderCommand, bool>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExpireOrderHandler> _logger;

    public ExpireOrderHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ILogger<ExpireOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<bool> Handle(ExpireOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        var transition = order.Cancel(request.Reason, CancelledBy.System, DateTimeOffset.UtcNow);
        if (transition != TransitionResult.Applied)
        {
            return false;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Expired stuck order {OrderId} with reason '{Reason}'", order.Id, request.Reason);
        return true;
    }
}
