using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using OrderService.Application.Abstractions;
using OrderService.Application.Exceptions;
using OrderService.Application.Orders.DTOs;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.PlaceOrder;

public sealed record PlaceOrderItemDto(
    Guid ProductId,
    int Quantity);

public sealed record PlaceOrderCommand(
    Guid UserId,
    string IdempotencyKey,
    AddressDto ShippingAddress,
    IReadOnlyList<PlaceOrderItemDto> Items,
    string Currency = "USD") : IRequest<PlaceOrderResult>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3);

        RuleFor(x => x.ShippingAddress)
            .NotNull();

        When(x => x.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.ShippingAddress.RecipientName)
                .NotEmpty()
                .MaximumLength(Address.MaxRecipientNameLength);

            RuleFor(x => x.ShippingAddress.Street)
                .NotEmpty()
                .MaximumLength(Address.MaxStreetLength);

            RuleFor(x => x.ShippingAddress.City)
                .NotEmpty()
                .MaximumLength(Address.MaxCityLength);

            RuleFor(x => x.ShippingAddress.Country)
                .NotEmpty()
                .Length(Address.CountryLength);
        });

        RuleFor(x => x.Items)
            .NotEmpty()
            .Must(items => items.Count <= Order.MaxLines)
            .WithMessage($"An order cannot contain more than {Order.MaxLines} item lines.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty();

            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(Quantity.MinValue, Quantity.MaxValue);
        });
    }
}

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrderRepository _orderRepository;
    private readonly IProductCatalogClient _productCatalogClient;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IUnitOfWork _unitOfWork;

    public PlaceOrderHandler(
        IOrderRepository orderRepository,
        IProductCatalogClient productCatalogClient,
        IIdempotencyStore idempotencyStore,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _productCatalogClient = productCatalogClient;
        _idempotencyStore = idempotencyStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(request);

        var idempotencyStart = await _idempotencyStore.TryStartAsync(
            request.UserId,
            request.IdempotencyKey,
            requestHash,
            cancellationToken);

        switch (idempotencyStart.Status)
        {
            case IdempotencyStartStatus.Replay:
                var cachedOrder = string.IsNullOrWhiteSpace(idempotencyStart.ResponseBody)
                    ? null
                    : JsonSerializer.Deserialize<OrderDetailsDto>(idempotencyStart.ResponseBody, JsonOptions);
                return new PlaceOrderResult(PlaceOrderOutcome.Replayed, cachedOrder);

            case IdempotencyStartStatus.HashMismatch:
                return new PlaceOrderResult(
                    PlaceOrderOutcome.IdempotencyKeyConflict,
                    ErrorMessage: "Idempotency-Key was already used with a different request payload.");

            case IdempotencyStartStatus.InProgress:
                return new PlaceOrderResult(
                    PlaceOrderOutcome.IdempotencyKeyInProgress,
                    ErrorMessage: "A request with this Idempotency-Key is currently being processed.");
        }

        try
        {
            var address = Address.Create(
                request.ShippingAddress.RecipientName,
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.Country);

            var mergedItems = request.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new PlaceOrderItemDto(g.Key, g.Sum(x => x.Quantity)))
                .ToList();

            var drafts = new List<OrderLineDraft>(mergedItems.Count);
            string? resolvedCurrency = null;

            foreach (var item in mergedItems)
            {
                var product = await _productCatalogClient.GetProductAsync(item.ProductId, cancellationToken);
                if (product is null || !product.IsActive)
                {
                    throw new ProductNotAvailableException(
                        item.ProductId,
                        $"Product '{item.ProductId}' does not exist or is not active.");
                }

                resolvedCurrency ??= product.Currency;
                var unitPrice = Money.Create(product.Price, product.Currency);
                var quantity = Quantity.Create(item.Quantity);
                drafts.Add(new OrderLineDraft(product.Id, product.Name, unitPrice, quantity));
            }

            var utcNow = DateTimeOffset.UtcNow;
            var order = Order.Place(
                request.UserId,
                address,
                drafts,
                resolvedCurrency ?? request.Currency,
                utcNow);

            _orderRepository.Add(order);

            var dto = MapToDetailsDto(order);
            var serializedResponse = JsonSerializer.Serialize(dto, JsonOptions);

            await _idempotencyStore.CompleteAsync(
                request.UserId,
                request.IdempotencyKey,
                201,
                serializedResponse,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PlaceOrderResult(PlaceOrderOutcome.Created, dto);
        }
        catch
        {
            await _idempotencyStore.AbandonAsync(request.UserId, request.IdempotencyKey, cancellationToken);
            throw;
        }
    }

    public static string ComputeRequestHash(PlaceOrderCommand request)
    {
        var canonical = new
        {
            request.UserId,
            RecipientName = request.ShippingAddress.RecipientName.Trim(),
            Street = request.ShippingAddress.Street.Trim(),
            City = request.ShippingAddress.City.Trim(),
            Country = request.ShippingAddress.Country.Trim().ToUpperInvariant(),
            Items = request.Items
                .GroupBy(i => i.ProductId)
                .OrderBy(g => g.Key)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToList()
        };

        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes);
    }

    private static OrderDetailsDto MapToDetailsDto(Order order)
    {
        var items = order.Lines
            .Select(l => new OrderLineDto(
                l.ProductId,
                l.ProductName,
                l.UnitPrice.Amount,
                l.UnitPrice.Currency,
                l.Quantity.Value,
                l.LineTotal.Amount))
            .ToList();

        var address = new AddressDto(
            order.ShippingAddress.RecipientName,
            order.ShippingAddress.Street,
            order.ShippingAddress.City,
            order.ShippingAddress.Country);

        var history = new List<StatusHistoryEntryDto>
        {
            new(order.Status.ToString(), order.PlacedAtUtc, null)
        };

        return new OrderDetailsDto(
            order.Id,
            order.UserId,
            order.Status.ToString(),
            order.StatusReason,
            order.CancelledBy?.ToString(),
            order.PaymentId,
            order.Total.Amount,
            order.Total.Currency,
            order.Lines.Sum(l => l.Quantity.Value),
            address,
            items,
            history,
            order.PlacedAtUtc,
            order.UpdatedAtUtc,
            order.Version);
    }
}
