using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Infrastructure.Persistence;

public sealed class ProductDbContext(
    DbContextOptions<ProductDbContext> options,
    IPublisher? publisher = null,
    ILogger<ProductDbContext>? logger = null) : DbContext(options), IUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);
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
            logger?.LogWarning(ex, "Optimistic concurrency conflict detected while saving Product changes.");
            throw new ConflictException(
                "The product was modified by another request. Please reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A product with the specified SKU already exists.");
        }
        catch (Exception)
        {
            // Clear tracked entities so that if the Retry policy re-runs the command handler,
            // the scoped DbContext does not contain stale/duplicate entities from the failed attempt.
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

