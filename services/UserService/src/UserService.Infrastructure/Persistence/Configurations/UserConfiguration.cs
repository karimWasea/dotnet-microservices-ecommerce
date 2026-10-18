using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.OwnsOne(u => u.Email, emailBuilder =>
        {
            emailBuilder.Property(e => e.Value)
                .HasColumnName("email")
                .HasMaxLength(Email.MaxLength)
                .IsRequired();

            emailBuilder.HasIndex(e => e.Value)
                .IsUnique()
                .HasDatabaseName("IX_users_email");
        });
        builder.Navigation(u => u.Email).IsRequired();

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(User.MaxDisplayNameLength)
            .IsRequired();

        builder.OwnsOne(u => u.PasswordHash, hashBuilder =>
        {
            hashBuilder.Property(h => h.Value)
                .HasColumnName("password_hash")
                .HasMaxLength(512)
                .IsRequired();
        });
        builder.Navigation(u => u.PasswordHash).IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.FailedLoginCount)
            .HasColumnName("failed_login_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.LockedOutUntilUtc)
            .HasColumnName("locked_out_until_utc");

        builder.Property(u => u.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc");

        builder.Property(u => u.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(u => u.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // PostgreSQL system column xmin as optimistic concurrency token.
        builder.Property<uint>("xmin")
            .IsRowVersion();

        builder.HasMany(u => u.RefreshTokens)
            .WithOne()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.RefreshTokens)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(u => u.DomainEvents);
    }
}
