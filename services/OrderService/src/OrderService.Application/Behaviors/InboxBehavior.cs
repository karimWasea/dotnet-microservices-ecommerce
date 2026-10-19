using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using OrderService.Domain.Abstractions;

namespace OrderService.Application.Behaviors;

/// <summary>
/// Ensures at-least-once RabbitMQ deliveries are processed idempotently per (MessageId, ConsumerName).
/// Marks the message as processed in the Inbox table and commits via IUnitOfWork in the same transaction
/// as the aggregate state change and outbox messages.
/// </summary>
public sealed class InboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IInboxMessage
{
    private readonly IInbox _inbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InboxBehavior<TRequest, TResponse>> _logger;

    public InboxBehavior(
        IInbox inbox,
        IUnitOfWork unitOfWork,
        ILogger<InboxBehavior<TRequest, TResponse>> logger)
    {
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (await _inbox.HasProcessedAsync(request.MessageId, request.ConsumerName, cancellationToken))
        {
            _logger.LogInformation(
                "Duplicate message {MessageId} for consumer {ConsumerName} skipped by InboxBehavior",
                request.MessageId,
                request.ConsumerName);
            return default!;
        }

        var response = await next();

        await _inbox.MarkProcessedAsync(request.MessageId, request.ConsumerName, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return response;
    }
}
