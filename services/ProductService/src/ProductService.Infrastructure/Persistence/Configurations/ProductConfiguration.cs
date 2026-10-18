using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductService.Domain.Products;

namespace ProductService.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("CK_products_price_positive", "price > 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(Product.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(Product.MaxDescriptionLength);

        builder.OwnsOne(p => p.Sku, skuBuilder =>
        {
            skuBuilder.Property(s => s.Value)
                .HasColumnName("sku")
                .HasMaxLength(Sku.MaxLength)
                .IsRequired();

            skuBuilder.HasIndex(s => s.Value)
                .IsUnique()
                .HasDatabaseName("IX_products_sku");
        });
        builder.Navigation(p => p.Sku).IsRequired();

        builder.OwnsOne(p => p.Price, priceBuilder =>
        {
            priceBuilder.Property(pr => pr.Amount)
                .HasColumnName("price")
                .HasPrecision(18, 2)
                .IsRequired();
        });
        builder.Navigation(p => p.Price).IsRequired();

        builder.Property(p => p.Category)
            .HasColumnName("category")
            .HasMaxLength(Product.MaxCategoryLength)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // Maps PostgreSQL's system xmin column as the optimistic concurrency token.
        builder.Property(p => p.Version)
            .IsRowVersion();

        builder.HasIndex(p => p.Category)
            .HasDatabaseName("IX_products_category");

        builder.Ignore(p => p.DomainEvents);
    }
}
