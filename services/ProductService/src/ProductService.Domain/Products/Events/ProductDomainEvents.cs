using ProductService.Domain.Abstractions;

namespace ProductService.Domain.Products.Events;

public sealed record ProductCreated(
    Guid ProductId,
    string Sku,
    decimal Price,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record ProductPriceChanged(
    Guid ProductId,
    decimal OldPrice,
    decimal NewPrice,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record ProductDeactivated(
    Guid ProductId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record ProductActivated(
    Guid ProductId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
