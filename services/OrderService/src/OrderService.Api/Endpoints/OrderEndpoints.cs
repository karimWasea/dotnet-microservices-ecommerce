using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Orders.Commands.CancelOrder;
using OrderService.Application.Orders.Commands.PlaceOrder;
using OrderService.Application.Orders.DTOs;
using OrderService.Application.Orders.Queries;

namespace OrderService.Api.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapPost("/", PlaceOrder)
            .WithName("PlaceOrder")
            .Produces<OrderDetailsDto>(StatusCodes.Status201Created)
            .Produces<OrderDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetOrderById)
            .WithName("GetOrderById")
            .Produces<OrderDetailsDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/mine", ListMyOrders)
            .WithName("ListMyOrders")
            .Produces<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", ListOrders)
            .WithName("ListOrders")
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .Produces<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public static async Task<IResult> PlaceOrder(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] PlaceOrderRequest body,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing Idempotency-Key Header",
                detail: "The 'Idempotency-Key' request header is required for placing an order.",
                extensions: new Dictionary<string, object?> { ["code"] = "order.missing_idempotency_key" });
        }

        var userId = GetUserId(user);
        var command = new PlaceOrderCommand(
            userId,
            idempotencyKey.Trim(),
            body.ShippingAddress,
            body.Items,
            string.IsNullOrWhiteSpace(body.Currency) ? "USD" : body.Currency.Trim());

        var result = await sender.Send(command, ct);

        return result.Outcome switch
        {
            PlaceOrderOutcome.Created =>
                Results.Created($"/api/orders/{result.Order!.Id}", result.Order),

            PlaceOrderOutcome.Replayed =>
                Results.Extensions.OkWithHeader(result.Order, "X-Idempotency-Replayed", "true"),

            PlaceOrderOutcome.IdempotencyKeyConflict =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Idempotency Key Conflict",
                    detail: result.ErrorMessage ?? "Idempotency key payload mismatch.",
                    extensions: new Dictionary<string, object?> { ["code"] = "order.idempotency_conflict" }),

            PlaceOrderOutcome.IdempotencyKeyInProgress =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Request In Progress",
                    detail: result.ErrorMessage ?? "A request with this Idempotency-Key is currently in progress.",
                    extensions: new Dictionary<string, object?> { ["code"] = "order.idempotency_in_progress" }),

            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static async Task<IResult> GetOrderById(
        Guid id,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var isAdmin = user.IsInRole("Admin");

        var result = await sender.Send(new GetOrderByIdQuery(id, userId, isAdmin), ct);

        return result.Outcome switch
        {
            GetOrderByIdOutcome.Found => Results.Ok(result.Order),
            GetOrderByIdOutcome.NotFound => Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Order Not Found",
                Detail = $"Order with id '{id}' was not found.",
                Extensions = { ["code"] = "order.not_found" }
            }),
            GetOrderByIdOutcome.Forbidden => Results.Forbid(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static async Task<IResult> ListMyOrders(
        ClaimsPrincipal user,
        ISender sender,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var result = await sender.Send(new ListMyOrdersQuery(userId, page, pageSize, status), ct);
        return Results.Ok(result);
    }

    public static async Task<IResult> ListOrders(
        ISender sender,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListOrdersQuery(page, pageSize, userId, status), ct);
        return Results.Ok(result);
    }

    public static async Task<IResult> CancelOrder(
        Guid id,
        [FromBody] CancelOrderRequest? body,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var isAdmin = user.IsInRole("Admin");

        var result = await sender.Send(new CancelOrderCommand(id, userId, isAdmin, body?.Reason), ct);

        return result.Outcome switch
        {
            CancelOrderOutcome.Cancelled => Results.NoContent(),
            CancelOrderOutcome.AlreadyCancelled => Results.NoContent(),
            CancelOrderOutcome.NotFound => Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Order Not Found",
                Detail = $"Order with id '{id}' was not found.",
                Extensions = { ["code"] = "order.not_found" }
            }),
            CancelOrderOutcome.Forbidden => Results.Forbid(),
            CancelOrderOutcome.ConflictCannotCancel => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cannot Cancel Order",
                detail: result.ErrorMessage ?? "Order cannot be cancelled in its current status.",
                extensions: new Dictionary<string, object?> { ["code"] = "order.cannot_cancel" }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (Guid.TryParse(claim, out var parsed))
        {
            return parsed;
        }

        throw new UnauthorizedAccessException("User ID is missing or invalid in JWT token.");
    }

    public sealed record PlaceOrderRequest(
        AddressDto ShippingAddress,
        IReadOnlyList<PlaceOrderItemDto> Items,
        string? Currency);

    public sealed record CancelOrderRequest(string? Reason);
}

public static class EndpointResultExtensions
{
    public static IResult OkWithHeader<T>(this IResultExtensions _, T value, string headerName, string headerValue)
    {
        return new HeaderResult<T>(value, headerName, headerValue);
    }

    private sealed class HeaderResult<T> : IResult
    {
        private readonly T _value;
        private readonly string _headerName;
        private readonly string _headerValue;

        public HeaderResult(T value, string headerName, string headerValue)
        {
            _value = value;
            _headerName = headerName;
            _headerValue = headerValue;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.TryAdd(_headerName, _headerValue);
            return httpContext.Response.WriteAsJsonAsync(_value);
        }
    }
}
