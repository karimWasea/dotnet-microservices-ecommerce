using MediatR;
using Microsoft.Extensions.Logging;
using ProductService.Domain.Products.Events;

namespace ProductService.Application.Products.EventHandlers;

public sealed class ProductCreatedEventHandler(
    ILogger<ProductCreatedEventHandler> logger) : INotificationHandler<ProductCreated>
{
    public Task Handle(ProductCreated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain Event ProductCreated: ProductId={ProductId}, Sku={Sku}, Price={Price}, OccurredOnUtc={OccurredOnUtc}",
            notification.ProductId,
            notification.Sku,
            notification.Price,
            notification.OccurredOnUtc);

        return Task.CompletedTask;
    }
}

public sealed class ProductPriceChangedEventHandler(
    ILogger<ProductPriceChangedEventHandler> logger) : INotificationHandler<ProductPriceChanged>
{
    public Task Handle(ProductPriceChanged notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain Event ProductPriceChanged: ProductId={ProductId}, OldPrice={OldPrice}, NewPrice={NewPrice}, OccurredOnUtc={OccurredOnUtc}",
            notification.ProductId,
            notification.OldPrice,
            notification.NewPrice,
            notification.OccurredOnUtc);

        return Task.CompletedTask;
    }
}

public sealed class ProductDeactivatedEventHandler(
    ILogger<ProductDeactivatedEventHandler> logger) : INotificationHandler<ProductDeactivated>
{
    public Task Handle(ProductDeactivated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain Event ProductDeactivated: ProductId={ProductId}, OccurredOnUtc={OccurredOnUtc}",
            notification.ProductId,
            notification.OccurredOnUtc);

        return Task.CompletedTask;
    }
}

public sealed class ProductActivatedEventHandler(
    ILogger<ProductActivatedEventHandler> logger) : INotificationHandler<ProductActivated>
{
    public Task Handle(ProductActivated notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain Event ProductActivated: ProductId={ProductId}, OccurredOnUtc={OccurredOnUtc}",
            notification.ProductId,
            notification.OccurredOnUtc);

        return Task.CompletedTask;
    }
}
