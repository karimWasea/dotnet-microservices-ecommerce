using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Abstractions;
using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Auth;
using OrderService.Infrastructure.HttpClients;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Messaging.Consumers;
using OrderService.Infrastructure.Read;
using OrderService.Infrastructure.Read.Projections;
using OrderService.Infrastructure.Write;
using OrderService.Infrastructure.Write.Repositories;

namespace OrderService.Infrastructure;

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

        var writeConnectionString = configuration.GetConnectionString("OrderWriteDb")
            ?? throw new InvalidOperationException("ConnectionString 'OrderWriteDb' is required.");

        var readConnectionString = configuration.GetConnectionString("OrderReadDb")
            ?? throw new InvalidOperationException("ConnectionString 'OrderReadDb' is required.");

        services.AddDbContext<OrderDbContext>(options =>
            options.UseNpgsql(writeConnectionString));

        services.AddDbContext<OrderReadDbContext>(options =>
            options.UseNpgsql(readConnectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderDbContext>());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IInbox, InboxStore>();
        services.AddScoped<IOrderReadService, OrderReadService>();
        services.AddScoped<OrderReadModelProjector>();

        // ProductCatalog HTTP client + Polly v8 Resilience
        var catalogOptions = new ProductCatalogOptions();
        configuration.GetSection(ProductCatalogOptions.SectionName).Bind(catalogOptions);
        services.Configure<ProductCatalogOptions>(configuration.GetSection(ProductCatalogOptions.SectionName));

        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddHttpClient<IProductCatalogClient, ProductCatalogHttpClient>(client =>
            {
                client.BaseAddress = new Uri(catalogOptions.BaseUrl);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddProductCatalogResiliencePipeline(catalogOptions);

        // JWKS provider
        services.Configure<JwtAuthOptions>(configuration.GetSection(JwtAuthOptions.SectionName));
        services.AddHttpClient<JwksSigningKeyProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<JwksSigningKeyProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var client = httpClientFactory.CreateClient(nameof(JwksSigningKeyProvider));
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtAuthOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwksSigningKeyProvider>>();
            return new JwksSigningKeyProvider(client, opts, logger);
        });

        // RabbitMQ messaging
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<RabbitMqTopologyInitializer>();
        services.AddHostedService<OutboxPublisherService>();
        services.AddHostedService<InventoryEventsConsumer>();
        services.AddHostedService<PaymentEventsConsumer>();
        services.AddHostedService<OrderReadModelConsumer>();

        return services;
    }
}
