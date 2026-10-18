using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UserService.Infrastructure.Persistence;

public sealed class UserDesignTimeDbContextFactory : IDesignTimeDbContextFactory<UserDbContext>
{
    public UserDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UserDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UserDb")
            ?? "Host=localhost;Port=5432;Database=userdb;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);

        return new UserDbContext(optionsBuilder.Options);
    }
}
