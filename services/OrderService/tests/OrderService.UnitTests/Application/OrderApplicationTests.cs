using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderService.Application.Abstractions;
using OrderService.Application.Behaviors;
using OrderService.Application.Exceptions;
using OrderService.Application.Orders.Commands.CancelOrder;
using OrderService.Application.Orders.Commands.PlaceOrder;
using OrderService.Application.Orders.Commands.Saga;
using OrderService.Application.Orders.DTOs;
using OrderService.Application.Orders.Queries;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;
using OrderService.Domain.Orders.Events;

namespace OrderService.UnitTests.Application;

public sealed class OrderApplicationTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IProductCatalogClient _productCatalogClient = Substitute.For<IProductCatalogClient>();
    private readonly IIdempotencyStore _idempotencyStore = Substitute.For<IIdempotencyStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static PlaceOrderCommand CreateValidPlaceOrderCommand(Guid? userId = null, Guid? productId = null)
    {
        return new PlaceOrderCommand(
            userId ?? Guid.NewGuid(),
            "idem-key-123",
            new AddressDto("Alice", "123 Main St", "Cairo", "EG"),
            [new PlaceOrderItemDto(productId ?? Guid.NewGuid(), 2)],
            "USD");
    }

    [Fact]
    public async Task PlaceOrder_HappyPath_CreatesOrder_CompletesIdempotency_AndSaves()
    {
        var productId = Guid.NewGuid();
        var cmd = CreateValidPlaceOrderCommand(productId: productId);

        _idempotencyStore.TryStartAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyStartResult(IdempotencyStartStatus.Started));

        _productCatalogClient.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .Returns(new ProductSnapshot(productId, "Mechanical Keyboard", 150.00m, "USD", true));

        var handler = new PlaceOrderHandler(_orderRepository, _productCatalogClient, _idempotencyStore, _unitOfWork);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(PlaceOrderOutcome.Created, result.Outcome);
        Assert.NotNull(result.Order);
        Assert.Equal(300.00m, result.Order!.Total);
        Assert.Equal("USD", result.Order.Currency);
        Assert.Equal("Pending", result.Order.Status);

        _orderRepository.Received(1).Add(Arg.Any<Order>());
        await _idempotencyStore.Received(1).CompleteAsync(
            cmd.UserId,
            cmd.IdempotencyKey,
            201,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WhenProductInactive_ThrowsProductNotAvailableAndAbandonsIdempotency()
    {
        var productId = Guid.NewGuid();
        var cmd = CreateValidPlaceOrderCommand(productId: productId);

        _idempotencyStore.TryStartAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyStartResult(IdempotencyStartStatus.Started));

        _productCatalogClient.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .Returns(new ProductSnapshot(productId, "Discontinued Item", 50.00m, "USD", false));

        var handler = new PlaceOrderHandler(_orderRepository, _productCatalogClient, _idempotencyStore, _unitOfWork);

        await Assert.ThrowsAsync<ProductNotAvailableException>(
            () => handler.Handle(cmd, CancellationToken.None));

        await _idempotencyStore.Received(1).AbandonAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WhenProductServiceUnavailable_AbandonsIdempotencyAndRethrows()
    {
        var productId = Guid.NewGuid();
        var cmd = CreateValidPlaceOrderCommand(productId: productId);

        _idempotencyStore.TryStartAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyStartResult(IdempotencyStartStatus.Started));

        _productCatalogClient.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DependencyUnavailableException("ProductService", "Circuit breaker open", 20));

        var handler = new PlaceOrderHandler(_orderRepository, _productCatalogClient, _idempotencyStore, _unitOfWork);

        await Assert.ThrowsAsync<DependencyUnavailableException>(
            () => handler.Handle(cmd, CancellationToken.None));

        await _idempotencyStore.Received(1).AbandonAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WhenIdempotencyReplay_ReturnsCachedOrderWithoutCallingProductService()
    {
        var cmd = CreateValidPlaceOrderCommand();
        var cachedDto = new OrderDetailsDto(
            Guid.NewGuid(),
            cmd.UserId,
            "Pending",
            null,
            null,
            null,
            300.00m,
            "USD",
            2,
            cmd.ShippingAddress,
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

        var json = JsonSerializer.Serialize(cachedDto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _idempotencyStore.TryStartAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyStartResult(IdempotencyStartStatus.Replay, 201, json));

        var handler = new PlaceOrderHandler(_orderRepository, _productCatalogClient, _idempotencyStore, _unitOfWork);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(PlaceOrderOutcome.Replayed, result.Outcome);
        Assert.NotNull(result.Order);
        Assert.Equal(cachedDto.Id, result.Order!.Id);
        await _productCatalogClient.DidNotReceiveWithAnyArgs().GetProductAsync(default, default);
        _orderRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task PlaceOrder_WhenIdempotencyHashMismatch_ReturnsConflictOutcome()
    {
        var cmd = CreateValidPlaceOrderCommand();
        _idempotencyStore.TryStartAsync(cmd.UserId, cmd.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyStartResult(IdempotencyStartStatus.HashMismatch));

        var handler = new PlaceOrderHandler(_orderRepository, _productCatalogClient, _idempotencyStore, _unitOfWork);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(PlaceOrderOutcome.IdempotencyKeyConflict, result.Outcome);
        await _productCatalogClient.DidNotReceiveWithAnyArgs().GetProductAsync(default, default);
    }

    [Fact]
    public async Task CancelOrder_OwnerAndAdminAndConflict_BehavesCorrectly()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var order = Order.Place(
            ownerId,
            Address.Create("Alice", "123 St", "Cairo", "EG"),
            [new OrderLineDraft(Guid.NewGuid(), "Item", Money.Create(10m, "USD"), Quantity.Create(1))],
            "USD",
            DateTimeOffset.UtcNow);

        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var handler = new CancelOrderHandler(_orderRepository, _unitOfWork);

        // Non-owner non-admin -> Forbidden
        var forbidden = await handler.Handle(
            new CancelOrderCommand(order.Id, otherUserId, false, "Try cancel"),
            CancellationToken.None);
        Assert.Equal(CancelOrderOutcome.Forbidden, forbidden.Outcome);

        // Owner -> Cancelled
        var cancelled = await handler.Handle(
            new CancelOrderCommand(order.Id, ownerId, false, "Changed mind"),
            CancellationToken.None);
        Assert.Equal(CancelOrderOutcome.Cancelled, cancelled.Outcome);

        // Second cancel -> AlreadyCancelled (idempotent)
        var alreadyCancelled = await handler.Handle(
            new CancelOrderCommand(order.Id, ownerId, false, "Changed mind"),
            CancellationToken.None);
        Assert.Equal(CancelOrderOutcome.AlreadyCancelled, alreadyCancelled.Outcome);
    }

    [Fact]
    public async Task InboxBehavior_WhenAlreadyProcessed_SkipsHandlerAndDoesNotSave()
    {
        var inbox = Substitute.For<IInbox>();
        var cmd = new RecordStockReservedCommand(Guid.NewGuid(), "InventoryEventsConsumer", Guid.NewGuid(), DateTimeOffset.UtcNow);
        inbox.HasProcessedAsync(cmd.MessageId, cmd.ConsumerName, Arg.Any<CancellationToken>()).Returns(true);

        var behavior = new InboxBehavior<RecordStockReservedCommand, TransitionResult>(
            inbox,
            _unitOfWork,
            NullLogger<InboxBehavior<RecordStockReservedCommand, TransitionResult>>.Instance);

        var handlerCalled = false;
        RequestHandlerDelegate<TransitionResult> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult(TransitionResult.Applied);
        };

        await behavior.Handle(cmd, next, CancellationToken.None);

        Assert.False(handlerCalled);
        await inbox.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task InboxBehavior_WhenNewMessage_RunsHandlerMarksProcessedAndSaves()
    {
        var inbox = Substitute.For<IInbox>();
        var cmd = new RecordStockReservedCommand(Guid.NewGuid(), "InventoryEventsConsumer", Guid.NewGuid(), DateTimeOffset.UtcNow);
        inbox.HasProcessedAsync(cmd.MessageId, cmd.ConsumerName, Arg.Any<CancellationToken>()).Returns(false);

        var behavior = new InboxBehavior<RecordStockReservedCommand, TransitionResult>(
            inbox,
            _unitOfWork,
            NullLogger<InboxBehavior<RecordStockReservedCommand, TransitionResult>>.Instance);

        var handlerCalled = false;
        RequestHandlerDelegate<TransitionResult> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult(TransitionResult.Applied);
        };

        var res = await behavior.Handle(cmd, next, CancellationToken.None);

        Assert.True(handlerCalled);
        Assert.Equal(TransitionResult.Applied, res);
        await inbox.Received(1).MarkProcessedAsync(cmd.MessageId, cmd.ConsumerName, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SagaHandlers_PaymentFailedAfterStockReserved_CancelsOrderBySystem()
    {
        var order = Order.Place(
            Guid.NewGuid(),
            Address.Create("Alice", "123 St", "Cairo", "EG"),
            [new OrderLineDraft(Guid.NewGuid(), "Item", Money.Create(100m, "USD"), Quantity.Create(1))],
            "USD",
            DateTimeOffset.UtcNow);
        order.MarkStockReserved(DateTimeOffset.UtcNow);
        order.ClearDomainEvents();

        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = new RecordPaymentFailedHandler(
            _orderRepository,
            NullLogger<RecordPaymentFailedHandler>.Instance);

        var transition = await handler.Handle(
            new RecordPaymentFailedCommand(Guid.NewGuid(), "PaymentEventsConsumer", order.Id, "Card declined", DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(TransitionResult.Applied, transition);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancelledBy.System, order.CancelledBy);

        var evt = Assert.Single(order.DomainEvents.OfType<OrderCancelledDomainEvent>());
        Assert.Equal("System", evt.CancelledBy);
        Assert.Contains("Card declined", evt.Reason);
    }

    [Fact]
    public void Validators_ValidatePlaceOrderAndListMyOrders()
    {
        var placeValidator = new PlaceOrderValidator();
        var invalidPlace = new PlaceOrderCommand(Guid.Empty, "", new AddressDto("", "", "", ""), []);
        var placeRes = placeValidator.Validate(invalidPlace);
        Assert.False(placeRes.IsValid);

        var listValidator = new ListMyOrdersValidator();
        var invalidList = new ListMyOrdersQuery(Guid.NewGuid(), 0, 500, "InvalidStatus");
        var listRes = listValidator.Validate(invalidList);
        Assert.False(listRes.IsValid);
    }
}
