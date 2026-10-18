using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Application.Abstractions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;
using ProductService.Infrastructure.Persistence;

namespace ProductService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ProductDb")
            ?? throw new InvalidOperationException("Connection string 'ProductDb' is not configured.");

        var commandTimeoutSeconds =
            int.TryParse(configuration["Database:CommandTimeoutSeconds"], out var configuredTimeout) && configuredTimeout > 0
                ? configuredTimeout
                : 15;

        services.AddDbContext<ProductDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.CommandTimeout(commandTimeoutSeconds)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProductDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductReadService, ProductReadService>();

        return services;
    }
}
