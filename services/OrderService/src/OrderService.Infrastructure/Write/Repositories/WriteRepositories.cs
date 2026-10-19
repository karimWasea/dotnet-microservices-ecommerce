using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Application.Abstractions;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Write.Models;

namespace OrderService.Infrastructure.Write.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderDbContext _dbContext;

    public OrderRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public void Add(Order order)
    {
        _dbContext.Orders.Add(order);
    }

    public async Task<IReadOnlyList<Guid>> GetStaleOrderIdsAsync(
        DateTimeOffset cutoffUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(o =>
                (o.Status == OrderStatus.Pending || o.Status == OrderStatus.StockReserved) &&
                o.UpdatedAtUtc <= cutoffUtc)
            .OrderBy(o => o.UpdatedAtUtc)
            .Take(limit)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
    }
}

public sealed class InboxStore : IInbox
{
    private readonly OrderDbContext _dbContext;

    public InboxStore(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        return await _dbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumerName, ct);
    }

    public Task MarkProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        _dbContext.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumerName,
            ProcessedOnUtc = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }
}

public sealed class IdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan StaleInProgressThreshold = TimeSpan.FromMinutes(2);

    private readonly OrderDbContext _dbContext;

    public IdempotencyStore(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyStartResult> TryStartAsync(
        Guid userId,
        string key,
        string requestHash,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);

        if (existing is not null)
        {
            if (existing.ExpiresAtUtc <= now ||
                (existing.ResponseStatusCode is null && existing.CreatedAtUtc.Add(StaleInProgressThreshold) <= now))
            {
                _dbContext.IdempotencyRecords.Remove(existing);
                await _dbContext.SaveChangesAsync(ct);
            }
            else
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    return new IdempotencyStartResult(IdempotencyStartStatus.HashMismatch);
                }

                if (existing.ResponseStatusCode is null)
                {
                    return new IdempotencyStartResult(IdempotencyStartStatus.InProgress);
                }

                return new IdempotencyStartResult(
                    IdempotencyStartStatus.Replay,
                    existing.ResponseStatusCode,
                    existing.ResponseBody);
            }
        }

        var record = new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Key = key,
            RequestHash = requestHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(KeyTtl)
        };

        _dbContext.IdempotencyRecords.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
            return new IdempotencyStartResult(IdempotencyStartStatus.Started);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _dbContext.Entry(record).State = EntityState.Detached;

            var concurrent = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);

            if (concurrent is not null && !string.Equals(concurrent.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new IdempotencyStartResult(IdempotencyStartStatus.HashMismatch);
            }

            if (concurrent?.ResponseStatusCode is not null)
            {
                return new IdempotencyStartResult(
                    IdempotencyStartStatus.Replay,
                    concurrent.ResponseStatusCode,
                    concurrent.ResponseBody);
            }

            return new IdempotencyStartResult(IdempotencyStartStatus.InProgress);
        }
    }

    public async Task CompleteAsync(
        Guid userId,
        string key,
        int responseStatusCode,
        string responseBody,
        CancellationToken ct = default)
    {
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);

        if (record is not null)
        {
            record.ResponseStatusCode = responseStatusCode;
            record.ResponseBody = responseBody;
        }
    }

    public async Task AbandonAsync(
        Guid userId,
        string key,
        CancellationToken ct = default)
    {
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);

        if (record is not null && record.ResponseStatusCode is null)
        {
            _dbContext.IdempotencyRecords.Remove(record);
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
