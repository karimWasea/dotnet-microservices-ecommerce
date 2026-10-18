using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Application.Behaviors;
using ProductService.Application.Monitoring;

namespace ProductService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        Action<CircuitBreakerOptions>? configureCircuitBreaker = null)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        var circuitBreakerOptions = new CircuitBreakerOptions();
        configureCircuitBreaker?.Invoke(circuitBreakerOptions);

        services.AddSingleton(circuitBreakerOptions);
        services.AddSingleton<ProductMetrics>();
        services.AddSingleton<CircuitBreakerRegistry>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(CircuitBreakerBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}

