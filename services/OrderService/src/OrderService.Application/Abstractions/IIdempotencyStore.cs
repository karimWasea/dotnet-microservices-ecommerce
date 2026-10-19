namespace OrderService.Application.Abstractions;

public enum IdempotencyStartStatus
{
    Started = 1,
    Replay = 2,
    InProgress = 3,
    HashMismatch = 4
}

public sealed record IdempotencyStartResult(
    IdempotencyStartStatus Status,
    int? ResponseStatusCode = null,
    string? ResponseBody = null);

public interface IIdempotencyStore
{
    Task<IdempotencyStartResult> TryStartAsync(
        Guid userId,
        string key,
        string requestHash,
        CancellationToken ct = default);

    Task CompleteAsync(
        Guid userId,
        string key,
        int responseStatusCode,
        string responseBody,
        CancellationToken ct = default);

    Task AbandonAsync(
        Guid userId,
        string key,
        CancellationToken ct = default);
}
