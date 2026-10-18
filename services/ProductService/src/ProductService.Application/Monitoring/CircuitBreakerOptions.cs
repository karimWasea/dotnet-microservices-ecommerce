namespace ProductService.Application.Monitoring;

public sealed class CircuitBreakerOptions
{
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 5;
    public int SamplingDurationSeconds { get; set; } = 30;
    public int BreakDurationSeconds { get; set; } = 15;
}
