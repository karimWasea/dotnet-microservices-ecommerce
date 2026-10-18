using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProductService.Infrastructure.Persistence;

public sealed class ProductDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ProductDb")
            ?? "Host=localhost;Port=5432;Database=productdb;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);

        return new ProductDbContext(optionsBuilder.Options);
    }
}
