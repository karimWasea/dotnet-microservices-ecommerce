using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.IntegrationEvents.Outgoing;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;
using OrderService.Domain.Orders.Events;
using OrderService.Infrastructure.Write.Configurations;
using OrderService.Infrastructure.Write.Models;

namespace OrderService.Infrastructure.Write;

public sealed class OrderDbContext : DbContext, IUnitOfWork
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPublisher? _publisher;
    private readonly ICorrelationIdAccessor? _correlationIdAccessor;

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public OrderDbContext(
        DbContextOptions<OrderDbContext> options,
        IPublisher? publisher = null,
        ICorrelationIdAccessor? correlationIdAccessor = null)
        : base(options)
    {
        _publisher = publisher;
        _correlationIdAccessor = correlationIdAccessor;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = ChangeTracker
            .Entries<AggregateRoot>()
            .SelectMany(e =>
            {
                var events = e.Entity.DomainEvents.ToList();
                e.Entity.ClearDomainEvents();
                return events;
            })
            .ToList();

        if (domainEvents.Count > 0)
        {
            var correlationId = _correlationIdAccessor?.CorrelationId;
            foreach (var domainEvent in domainEvents)
            {
                if (_publisher is not null)
                {
                    await _publisher.Publish(domainEvent, cancellationToken);
                }

                var outboxMessage = MapToOutboxMessage(domainEvent, correlationId);
                if (outboxMessage is not null)
                {
                    OutboxMessages.Add(outboxMessage);
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private static OutboxMessage? MapToOutboxMessage(IDomainEvent domainEvent, string? correlationId)
    {
        var now = DateTimeOffset.UtcNow;

        return domainEvent switch
        {
            OrderPlacedDomainEvent e => new OutboxMessage
            {
                Id = Guid.CreateVersion7(e.OccurredOnUtc),
                Type = OrderPlacedIntegrationEvent.RoutingKey,
                Payload = JsonSerializer.Serialize(
                    new OrderPlacedIntegrationEvent(
                        e.OrderId,
                        e.UserId,
                        e.Version,
                        e.Status,
                        e.Items.Select(i => new OrderLineIntegrationDto(
                            i.ProductId,
                            i.ProductName,
                            i.UnitPrice,
                            i.Quantity,
                            i.LineTotal)).ToList(),
                        new AddressIntegrationDto(
                            e.ShippingAddress.RecipientName,
                            e.ShippingAddress.Street,
                            e.ShippingAddress.City,
                            e.ShippingAddress.Country),
                        e.Total,
                        e.Currency,
                        e.OccurredOnUtc),
                    JsonOptions),
                CorrelationId = correlationId,
                OccurredOnUtc = e.OccurredOnUtc
            },

            OrderStockReservedDomainEvent e => new OutboxMessage
            {
                Id = Guid.CreateVersion7(e.OccurredOnUtc),
                Type = OrderStockReservedIntegrationEvent.RoutingKey,
                Payload = JsonSerializer.Serialize(
                    new OrderStockReservedIntegrationEvent(
                        e.OrderId,
                        e.UserId,
                        e.Version,
                        e.OccurredOnUtc),
                    JsonOptions),
                CorrelationId = correlationId,
                OccurredOnUtc = e.OccurredOnUtc
            },

            OrderConfirmedDomainEvent e => new OutboxMessage
            {
                Id = Guid.CreateVersion7(e.OccurredOnUtc),
                Type = OrderConfirmedIntegrationEvent.RoutingKey,
                Payload = JsonSerializer.Serialize(
                    new OrderConfirmedIntegrationEvent(
                        e.OrderId,
                        e.UserId,
                        e.Version,
                        e.Total,
                        e.Currency,
                        e.PaymentId,
                        e.OccurredOnUtc),
                    JsonOptions),
                CorrelationId = correlationId,
                OccurredOnUtc = e.OccurredOnUtc
            },

            OrderRejectedDomainEvent e => new OutboxMessage
            {
                Id = Guid.CreateVersion7(e.OccurredOnUtc),
                Type = OrderRejectedIntegrationEvent.RoutingKey,
                Payload = JsonSerializer.Serialize(
                    new OrderRejectedIntegrationEvent(
                        e.OrderId,
                        e.UserId,
                        e.Version,
                        e.Reason,
                        e.OccurredOnUtc),
                    JsonOptions),
                CorrelationId = correlationId,
                OccurredOnUtc = e.OccurredOnUtc
            },

            OrderCancelledDomainEvent e => new OutboxMessage
            {
                Id = Guid.CreateVersion7(e.OccurredOnUtc),
                Type = OrderCancelledIntegrationEvent.RoutingKey,
                Payload = JsonSerializer.Serialize(
                    new OrderCancelledIntegrationEvent(
                        e.OrderId,
                        e.UserId,
                        e.Version,
                        e.Reason,
                        e.CancelledBy,
                        e.OccurredOnUtc),
                    JsonOptions),
                CorrelationId = correlationId,
                OccurredOnUtc = e.OccurredOnUtc
            },

            _ => null
        };
    }
}
