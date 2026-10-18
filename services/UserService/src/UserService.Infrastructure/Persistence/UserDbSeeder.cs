using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UserService.Application.Abstractions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Persistence;

public sealed class UserDbSeeder(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<UserDbSeeder> logger)
{
    public const string DefaultAdminEmail = "admin@ecommerce.local";
    private const string DevFallbackAdminPassword = "Admin@123456!";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var adminEmail = Email.Create(DefaultAdminEmail);

        if (await userRepository.ExistsByEmailAsync(adminEmail, cancellationToken))
        {
            return;
        }

        var adminPassword = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            adminPassword = DevFallbackAdminPassword;
            logger.LogWarning(
                "Seed:AdminPassword is not configured. Using the Development fallback password for {AdminEmail}.",
                DefaultAdminEmail);
        }

        var passwordHash = PasswordHash.Create(passwordHasher.Hash(adminPassword));
        var adminUser = User.Register(
            adminEmail,
            "System Administrator",
            passwordHash,
            Role.Admin,
            timeProvider.GetUtcNow());

        userRepository.Add(adminUser);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded default Development Admin user {AdminEmail} (UserId={UserId}).",
            DefaultAdminEmail,
            adminUser.Id);
    }
}
