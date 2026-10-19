using FluentValidation;
using MediatR;
using OrderService.Application.Abstractions;
using OrderService.Application.Orders.DTOs;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Queries;

public enum GetOrderByIdOutcome
{
    Found = 1,
    NotFound = 2,
    Forbidden = 3
}

public sealed record GetOrderByIdResult(
    GetOrderByIdOutcome Outcome,
    OrderDetailsDto? Order = null);

public sealed record GetOrderByIdQuery(
    Guid OrderId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest<GetOrderByIdResult>;

public sealed class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, GetOrderByIdResult>
{
    private readonly IOrderReadService _readService;

    public GetOrderByIdHandler(IOrderReadService readService)
    {
        _readService = readService;
    }

    public async Task<GetOrderByIdResult> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _readService.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return new GetOrderByIdResult(GetOrderByIdOutcome.NotFound);
        }

        if (!request.IsAdmin && order.UserId != request.RequestingUserId)
        {
            return new GetOrderByIdResult(GetOrderByIdOutcome.Forbidden);
        }

        return new GetOrderByIdResult(GetOrderByIdOutcome.Found, order);
    }
}

public sealed record ListMyOrdersQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    string? Status = null) : IRequest<PagedResult<OrderSummaryDto>>;

public sealed class ListMyOrdersValidator : AbstractValidator<ListMyOrdersQuery>
{
    public ListMyOrdersValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .Must(BeValidStatusOrNull)
            .WithMessage("Status must be one of: Pending, StockReserved, Confirmed, Rejected, Cancelled.");
    }

    private static bool BeValidStatusOrNull(string? status) =>
        string.IsNullOrWhiteSpace(status) || Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
}

public sealed class ListMyOrdersHandler : IRequestHandler<ListMyOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IOrderReadService _readService;

    public ListMyOrdersHandler(IOrderReadService readService)
    {
        _readService = readService;
    }

    public Task<PagedResult<OrderSummaryDto>> Handle(ListMyOrdersQuery request, CancellationToken cancellationToken)
    {
        return _readService.ListByUserAsync(
            request.UserId,
            request.Page,
            request.PageSize,
            request.Status,
            cancellationToken);
    }
}

public sealed record ListOrdersQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? UserId = null,
    string? Status = null) : IRequest<PagedResult<OrderSummaryDto>>;

public sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .Must(BeValidStatusOrNull)
            .WithMessage("Status must be one of: Pending, StockReserved, Confirmed, Rejected, Cancelled.");
    }

    private static bool BeValidStatusOrNull(string? status) =>
        string.IsNullOrWhiteSpace(status) || Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
}

public sealed class ListOrdersHandler : IRequestHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IOrderReadService _readService;

    public ListOrdersHandler(IOrderReadService readService)
    {
        _readService = readService;
    }

    public Task<PagedResult<OrderSummaryDto>> Handle(ListOrdersQuery request, CancellationToken cancellationToken)
    {
        return _readService.ListAllAsync(
            request.Page,
            request.PageSize,
            request.UserId,
            request.Status,
            cancellationToken);
    }
}
