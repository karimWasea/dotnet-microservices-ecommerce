namespace ProductService.Application.Monitoring;

public sealed class CircuitBreakerOptions
{
    // Circuit Breaker settings
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 5;
    public int SamplingDurationSeconds { get; set; } = 30;
    public int BreakDurationSeconds { get; set; } = 15;

    // Retry settings
    public int MaxRetryAttempts { get; set; } = 2;
    public int RetryBaseDelayMs { get; set; } = 100;
    public bool RetryUseJitter { get; set; } = true;

    // Bulkhead (concurrency isolation) settings
    public int BulkheadMaxParallelization { get; set; } = 32;
    public int BulkheadMaxQueuedActions { get; set; } = 16;
}
