using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Persistence;

public sealed class UserDbContext(
    DbContextOptions<UserDbContext> options,
    IPublisher? publisher = null,
    ILogger<UserDbContext>? logger = null) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync instead of synchronous SaveChanges.");

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var aggregatesWithEvents = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count != 0)
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = aggregatesWithEvents
            .SelectMany(a => a.DomainEvents)
            .ToList();

        int result;
        try
        {
            result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger?.LogWarning(ex, "Optimistic concurrency conflict detected while saving User changes.");
            throw new ConflictException(
                "The user record was modified by another request. Please reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A user with the specified email or token already exists.");
        }
        catch (Exception)
        {
            ChangeTracker.Clear();
            throw;
        }

        foreach (var aggregate in aggregatesWithEvents)
        {
            aggregate.ClearDomainEvents();
        }

        if (publisher is not null)
        {
            foreach (var domainEvent in domainEvents)
            {
                try
                {
                    await publisher.Publish(domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger?.LogError(
                        ex,
                        "Error handling domain event {EventType} after database commit.",
                        domainEvent.GetType().Name);
                }
            }
        }

        return result;
    }
}
