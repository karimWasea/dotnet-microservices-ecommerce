using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Write.Models;

namespace OrderService.Infrastructure.Write.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.HasIndex(o => o.UserId)
            .HasDatabaseName("ix_orders_user_id");

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(o => o.Status)
            .HasDatabaseName("ix_orders_status");

        builder.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.RecipientName)
                .HasColumnName("recipient_name")
                .HasMaxLength(Address.MaxRecipientNameLength)
                .IsRequired();

            address.Property(a => a.Street)
                .HasColumnName("street")
                .HasMaxLength(Address.MaxStreetLength)
                .IsRequired();

            address.Property(a => a.City)
                .HasColumnName("city")
                .HasMaxLength(Address.MaxCityLength)
                .IsRequired();

            address.Property(a => a.Country)
                .HasColumnName("country")
                .HasMaxLength(Address.CountryLength)
                .IsFixedLength()
                .IsRequired();
        });

        builder.OwnsOne(o => o.Total, total =>
        {
            total.Property(t => t.Amount)
                .HasColumnName("total_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            total.Property(t => t.Currency)
                .HasColumnName("currency")
                .HasMaxLength(Money.CurrencyLength)
                .IsFixedLength()
                .IsRequired();
        });

        builder.Property(o => o.StatusReason)
            .HasColumnName("status_reason")
            .HasMaxLength(Order.MaxReasonLength);

        builder.Property(o => o.CancelledBy)
            .HasColumnName("cancelled_by")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.PaymentId)
            .HasColumnName("payment_id")
            .HasMaxLength(100);

        builder.Property(o => o.PlacedAtUtc)
            .HasColumnName("placed_at_utc")
            .IsRequired();

        builder.Property(o => o.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(o => o.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(o => o.DomainEvents);
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(l => l.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.HasIndex(l => l.OrderId)
            .HasDatabaseName("ix_order_lines_order_id");

        builder.Property(l => l.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(l => l.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(OrderLine.MaxProductNameLength)
            .IsRequired();

        builder.OwnsOne(l => l.UnitPrice, price =>
        {
            price.Property(p => p.Amount)
                .HasColumnName("unit_price")
                .HasPrecision(18, 2)
                .IsRequired();

            price.Property(p => p.Currency)
                .HasColumnName("currency")
                .HasMaxLength(Money.CurrencyLength)
                .IsFixedLength()
                .IsRequired();
        });

        builder.Property(l => l.Quantity)
            .HasColumnName("quantity")
            .HasConversion(q => q.Value, v => Quantity.Create(v))
            .IsRequired();

        builder.OwnsOne(l => l.LineTotal, total =>
        {
            total.Property(t => t.Amount)
                .HasColumnName("line_total")
                .HasPrecision(18, 2)
                .IsRequired();

            total.Property(t => t.Currency)
                .HasColumnName("line_total_currency")
                .HasMaxLength(Money.CurrencyLength)
                .IsFixedLength()
                .IsRequired();
        });
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(m => m.Type)
            .HasColumnName("type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(m => m.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(64);

        builder.Property(m => m.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();

        builder.Property(m => m.ProcessedOnUtc)
            .HasColumnName("processed_on_utc");

        builder.Property(m => m.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(m => m.NextRetryAtUtc)
            .HasColumnName("next_retry_at_utc");

        builder.Property(m => m.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(1000);

        builder.HasIndex(m => new { m.NextRetryAtUtc, m.OccurredOnUtc })
            .HasDatabaseName("ix_outbox_messages_unprocessed")
            .HasFilter("processed_on_utc IS NULL AND attempts < 10");
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(m => new { m.MessageId, m.Consumer });

        builder.Property(m => m.MessageId)
            .HasColumnName("message_id")
            .ValueGeneratedNever();

        builder.Property(m => m.Consumer)
            .HasColumnName("consumer")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.ProcessedOnUtc)
            .HasColumnName("processed_on_utc")
            .IsRequired();
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(r => r.Key)
            .HasColumnName("key")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(r => new { r.UserId, r.Key })
            .IsUnique()
            .HasDatabaseName("ux_idempotency_keys_user_id_key");

        builder.Property(r => r.RequestHash)
            .HasColumnName("request_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ResponseStatusCode)
            .HasColumnName("response_status_code");

        builder.Property(r => r.ResponseBody)
            .HasColumnName("response_body")
            .HasColumnType("jsonb");

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(r => r.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();
    }
}
