using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserService.Application.Abstractions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;
using UserService.Infrastructure.Persistence;
using UserService.Infrastructure.Security;

namespace UserService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UserDb")
            ?? throw new InvalidOperationException("Connection string 'UserDb' is not configured.");

        var commandTimeoutSeconds =
            int.TryParse(configuration["Database:CommandTimeoutSeconds"], out var configuredTimeout) && configuredTimeout > 0
                ? configuredTimeout
                : 15;

        services.AddDbContext<UserDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.CommandTimeout(commandTimeoutSeconds)));

        services.AddSingleton(TimeProvider.System);

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<RsaKeyMaterial>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UserDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserReadService, UserReadService>();
        services.AddScoped<UserDbSeeder>();

        return services;
    }
}
