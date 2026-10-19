namespace OrderService.Infrastructure.Read.Models;

public sealed class OrderSummaryReadModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public DateTimeOffset PlacedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

public sealed class OrderDetailsReadModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? CancelledBy { get; set; }
    public string? PaymentId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string ShippingAddressJson { get; set; } = "{}";
    public string ItemsJson { get; set; } = "[]";
    public string HistoryJson { get; set; } = "[]";
    public DateTimeOffset PlacedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}
