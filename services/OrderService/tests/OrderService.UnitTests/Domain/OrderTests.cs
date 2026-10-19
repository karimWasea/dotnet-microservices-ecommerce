using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;
using OrderService.Domain.Orders.Events;

namespace OrderService.UnitTests.Domain;

public sealed class OrderTests
{
    private static readonly DateTimeOffset BaseUtcNow = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid DefaultUserId = Guid.CreateVersion7(BaseUtcNow);
    private static readonly Address DefaultAddress = Address.Create("Alice", "123 Nile St", "Cairo", "EG");

    private static Order CreateDefaultOrder(OrderStatus targetStatus = OrderStatus.Pending)
    {
        var product1 = Guid.CreateVersion7(BaseUtcNow);
        var product2 = Guid.CreateVersion7(BaseUtcNow.AddSeconds(1));

        var order = Order.Place(
            DefaultUserId,
            DefaultAddress,
            [
                new OrderLineDraft(product1, "Keyboard", Money.Create(1500m, "EGP"), Quantity.Create(2)),
                new OrderLineDraft(product2, "Mouse", Money.Create(500m, "EGP"), Quantity.Create(1))
            ],
            "EGP",
            BaseUtcNow);

        switch (targetStatus)
        {
            case OrderStatus.StockReserved:
                order.MarkStockReserved(BaseUtcNow.AddSeconds(10));
                break;
            case OrderStatus.Confirmed:
                order.MarkStockReserved(BaseUtcNow.AddSeconds(10));
                order.Confirm("pay_123", BaseUtcNow.AddSeconds(20));
                break;
            case OrderStatus.Rejected:
                order.Reject("Out of stock", BaseUtcNow.AddSeconds(10));
                break;
            case OrderStatus.Cancelled:
                order.Cancel("Customer changed mind", CancelledBy.Customer, BaseUtcNow.AddSeconds(10));
                break;
        }

        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public void Place_WithValidLines_SetsPendingVersion1CalculatesTotalAndRaisesOrderPlaced()
    {
        var product1 = Guid.CreateVersion7(BaseUtcNow);
        var product2 = Guid.CreateVersion7(BaseUtcNow.AddSeconds(1));

        var order = Order.Place(
            DefaultUserId,
            DefaultAddress,
            [
                new OrderLineDraft(product1, "Keyboard", Money.Create(1500m, "EGP"), Quantity.Create(2)),
                new OrderLineDraft(product2, "Mouse", Money.Create(500m, "EGP"), Quantity.Create(1))
            ],
            "EGP",
            BaseUtcNow);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal(3500m, order.Total.Amount);
        Assert.Equal("EGP", order.Total.Currency);
        Assert.Equal(2, order.Lines.Count);

        var evt = Assert.Single(order.DomainEvents);
        var placed = Assert.IsType<OrderPlacedDomainEvent>(evt);
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal(1, placed.Version);
        Assert.Equal(3500m, placed.Total);
        Assert.Equal(2, placed.Items.Count);
    }

    [Fact]
    public void Place_WithEmptyLinesOrMoreThan20Lines_ThrowsDomainException()
    {
        var emptyEx = Assert.Throws<DomainException>(() =>
            Order.Place(DefaultUserId, DefaultAddress, [], "EGP", BaseUtcNow));
        Assert.Equal("order.invalid_line_count", emptyEx.Code);

        var twentyOneLines = Enumerable.Range(1, 21)
            .Select(i => new OrderLineDraft(
                Guid.NewGuid(),
                $"Product {i}",
                Money.Create(10m, "EGP"),
                Quantity.Create(1)))
            .ToList();

        var maxEx = Assert.Throws<DomainException>(() =>
            Order.Place(DefaultUserId, DefaultAddress, twentyOneLines, "EGP", BaseUtcNow));
        Assert.Equal("order.invalid_line_count", maxEx.Code);
    }

    [Fact]
    public void Place_WithDuplicateProductId_ThrowsDomainException()
    {
        var sameProductId = Guid.NewGuid();
        var lines = new[]
        {
            new OrderLineDraft(sameProductId, "Keyboard", Money.Create(100m, "EGP"), Quantity.Create(1)),
            new OrderLineDraft(sameProductId, "Keyboard", Money.Create(100m, "EGP"), Quantity.Create(2))
        };

        var ex = Assert.Throws<DomainException>(() =>
            Order.Place(DefaultUserId, DefaultAddress, lines, "EGP", BaseUtcNow));

        Assert.Equal("order.duplicate_product", ex.Code);
    }

    [Fact]
    public void Place_WithMismatchedCurrency_ThrowsDomainException()
    {
        var lines = new[]
        {
            new OrderLineDraft(Guid.NewGuid(), "Keyboard", Money.Create(100m, "USD"), Quantity.Create(1))
        };

        var ex = Assert.Throws<DomainException>(() =>
            Order.Place(DefaultUserId, DefaultAddress, lines, "EGP", BaseUtcNow));

        Assert.Equal("order.currency_mismatch", ex.Code);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, TransitionResult.Applied, OrderStatus.StockReserved, true)]
    [InlineData(OrderStatus.StockReserved, TransitionResult.NoChange, OrderStatus.StockReserved, false)]
    [InlineData(OrderStatus.Confirmed, TransitionResult.NoChange, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Rejected, TransitionResult.NotAllowed, OrderStatus.Rejected, false)]
    [InlineData(OrderStatus.Cancelled, TransitionResult.NotAllowed, OrderStatus.Cancelled, false)]
    public void MarkStockReserved_FollowsTransitionTable(
        OrderStatus initialStatus,
        TransitionResult expectedResult,
        OrderStatus expectedFinalStatus,
        bool shouldIncrementVersion)
    {
        var order = CreateDefaultOrder(initialStatus);
        var versionBefore = order.Version;

        var result = order.MarkStockReserved(BaseUtcNow.AddMinutes(1));

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedFinalStatus, order.Status);

        if (shouldIncrementVersion)
        {
            Assert.Equal(versionBefore + 1, order.Version);
            var evt = Assert.Single(order.DomainEvents);
            var reserved = Assert.IsType<OrderStockReservedDomainEvent>(evt);
            Assert.Equal(order.Version, reserved.Version);
        }
        else
        {
            Assert.Equal(versionBefore, order.Version);
            Assert.Empty(order.DomainEvents);
        }
    }

    [Theory]
    [InlineData(OrderStatus.Pending, TransitionResult.Applied, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.StockReserved, TransitionResult.Applied, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, TransitionResult.NoChange, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Rejected, TransitionResult.NotAllowed, OrderStatus.Rejected, false)]
    [InlineData(OrderStatus.Cancelled, TransitionResult.NotAllowed, OrderStatus.Cancelled, false)]
    public void Confirm_FollowsTransitionTable(
        OrderStatus initialStatus,
        TransitionResult expectedResult,
        OrderStatus expectedFinalStatus,
        bool shouldIncrementVersion)
    {
        var order = CreateDefaultOrder(initialStatus);
        var versionBefore = order.Version;

        var result = order.Confirm("pay_999", BaseUtcNow.AddMinutes(2));

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedFinalStatus, order.Status);

        if (shouldIncrementVersion)
        {
            Assert.Equal(versionBefore + 1, order.Version);
            var evt = Assert.Single(order.DomainEvents);
            var confirmed = Assert.IsType<OrderConfirmedDomainEvent>(evt);
            Assert.Equal(order.Version, confirmed.Version);
            Assert.Equal("pay_999", confirmed.PaymentId);
        }
        else
        {
            Assert.Equal(versionBefore, order.Version);
            Assert.Empty(order.DomainEvents);
        }
    }

    [Theory]
    [InlineData(OrderStatus.Pending, TransitionResult.Applied, OrderStatus.Rejected, true)]
    [InlineData(OrderStatus.Rejected, TransitionResult.NoChange, OrderStatus.Rejected, false)]
    [InlineData(OrderStatus.StockReserved, TransitionResult.NotAllowed, OrderStatus.StockReserved, false)]
    [InlineData(OrderStatus.Confirmed, TransitionResult.NotAllowed, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Cancelled, TransitionResult.NotAllowed, OrderStatus.Cancelled, false)]
    public void Reject_FollowsTransitionTable(
        OrderStatus initialStatus,
        TransitionResult expectedResult,
        OrderStatus expectedFinalStatus,
        bool shouldIncrementVersion)
    {
        var order = CreateDefaultOrder(initialStatus);
        var versionBefore = order.Version;

        var result = order.Reject("Insufficient stock", BaseUtcNow.AddMinutes(1));

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedFinalStatus, order.Status);

        if (shouldIncrementVersion)
        {
            Assert.Equal(versionBefore + 1, order.Version);
            var evt = Assert.Single(order.DomainEvents);
            var rejected = Assert.IsType<OrderRejectedDomainEvent>(evt);
            Assert.Equal(order.Version, rejected.Version);
        }
        else
        {
            Assert.Equal(versionBefore, order.Version);
            Assert.Empty(order.DomainEvents);
        }
    }

    [Theory]
    [InlineData(OrderStatus.Pending, TransitionResult.Applied, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.StockReserved, TransitionResult.Applied, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Cancelled, TransitionResult.NoChange, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Confirmed, TransitionResult.NotAllowed, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Rejected, TransitionResult.NotAllowed, OrderStatus.Rejected, false)]
    public void Cancel_FollowsTransitionTable(
        OrderStatus initialStatus,
        TransitionResult expectedResult,
        OrderStatus expectedFinalStatus,
        bool shouldIncrementVersion)
    {
        var order = CreateDefaultOrder(initialStatus);
        var versionBefore = order.Version;

        var result = order.Cancel("PaymentFailed", CancelledBy.System, BaseUtcNow.AddMinutes(1));

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedFinalStatus, order.Status);

        if (shouldIncrementVersion)
        {
            Assert.Equal(versionBefore + 1, order.Version);
            var evt = Assert.Single(order.DomainEvents);
            var cancelled = Assert.IsType<OrderCancelledDomainEvent>(evt);
            Assert.Equal(order.Version, cancelled.Version);
            Assert.Equal("System", cancelled.CancelledBy);
        }
        else
        {
            Assert.Equal(versionBefore, order.Version);
            Assert.Empty(order.DomainEvents);
        }
    }
}
