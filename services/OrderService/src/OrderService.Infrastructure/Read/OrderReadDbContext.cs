using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Infrastructure.Read.Models;

namespace OrderService.Infrastructure.Read;

public sealed class OrderReadDbContext : DbContext
{
    public DbSet<OrderSummaryReadModel> OrderSummaries => Set<OrderSummaryReadModel>();
    public DbSet<OrderDetailsReadModel> OrderDetails => Set<OrderDetailsReadModel>();

    public OrderReadDbContext(DbContextOptions<OrderReadDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderSummaryReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new OrderDetailsReadModelConfiguration());
    }
}

public sealed class OrderSummaryReadModelConfiguration : IEntityTypeConfiguration<OrderSummaryReadModel>
{
    public void Configure(EntityTypeBuilder<OrderSummaryReadModel> builder)
    {
        builder.ToTable("order_summaries");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Total)
            .HasColumnName("total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ItemCount)
            .HasColumnName("item_count")
            .IsRequired();

        builder.Property(x => x.PlacedAtUtc)
            .HasColumnName("placed_at_utc")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.PlacedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_order_summaries_user_id_placed_at");

        builder.HasIndex(x => new { x.Status, x.PlacedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("ix_order_summaries_status_placed_at");
    }
}

public sealed class OrderDetailsReadModelConfiguration : IEntityTypeConfiguration<OrderDetailsReadModel>
{
    public void Configure(EntityTypeBuilder<OrderDetailsReadModel> builder)
    {
        builder.ToTable("order_details");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.StatusReason)
            .HasColumnName("status_reason")
            .HasMaxLength(200);

        builder.Property(x => x.CancelledBy)
            .HasColumnName("cancelled_by")
            .HasMaxLength(20);

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .HasMaxLength(100);

        builder.Property(x => x.Total)
            .HasColumnName("total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.ItemCount)
            .HasColumnName("item_count")
            .IsRequired();

        builder.Property(x => x.ShippingAddressJson)
            .HasColumnName("shipping_address")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.ItemsJson)
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.HistoryJson)
            .HasColumnName("history")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.PlacedAtUtc)
            .HasColumnName("placed_at_utc")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();
    }
}
