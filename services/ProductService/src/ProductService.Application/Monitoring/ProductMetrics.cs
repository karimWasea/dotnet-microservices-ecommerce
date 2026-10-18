using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace ProductService.Application.Monitoring;

public sealed record MonitoringSnapshot(
    string CircuitState,
    long CircuitOpenedCount,
    DateTimeOffset? LastCircuitStateChangedAtUtc,
    string? LastCircuitFailureReason,
    long TotalRequests,
    long SucceededRequests,
    long FailedRequests,
    long CanceledRequests,
    long RejectedByCircuitBreakerRequests);

public sealed class ProductMetrics : IDisposable
{
    public const string MeterName = "ProductService";
    public const string ActivitySourceName = "ProductService";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName, "1.0.0");

    private readonly Counter<long> _requestsCounter;
    private readonly Histogram<double> _requestDurationMs;
    private readonly Counter<long> _circuitStateChangesCounter;

    private long _totalRequests;
    private long _succeededRequests;
    private long _failedRequests;
    private long _canceledRequests;
    private long _rejectedByCircuitBreakerRequests;
    private long _circuitOpenedCount;

    private volatile string _circuitState = "Closed";
    private DateTimeOffset? _lastCircuitStateChangedAtUtc;
    private volatile string? _lastCircuitFailureReason;

    public ProductMetrics()
    {
        _requestsCounter = _meter.CreateCounter<long>(
            "productservice.requests.total",
            description: "Total number of MediatR commands and queries processed.");

        _requestDurationMs = _meter.CreateHistogram<double>(
            "productservice.requests.duration",
            unit: "ms",
            description: "Execution duration of MediatR commands and queries in milliseconds.");

        _circuitStateChangesCounter = _meter.CreateCounter<long>(
            "productservice.circuit_breaker.state_changes",
            description: "Number of circuit breaker state transitions.");

        _meter.CreateObservableGauge<int>(
            "productservice.circuit_breaker.state",
            observeValue: () => _circuitState switch
            {
                "Closed" => 0,
                "HalfOpen" => 1,
                "Open" => 2,
                _ => 0
            },
            description: "Current circuit breaker state (0 = Closed, 1 = HalfOpen, 2 = Open).");
    }

    public string CircuitState => _circuitState;

    public void RecordRequestCompleted(string requestName, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _succeededRequests);
        _requestsCounter.Add(1, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "succeeded"));
        _requestDurationMs.Record(durationMs, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "succeeded"));
    }

    public void RecordRequestCanceled(string requestName, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _canceledRequests);
        _requestsCounter.Add(1, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "canceled"));
        _requestDurationMs.Record(durationMs, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "canceled"));
    }

    public void RecordRequestFailed(string requestName, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);
        _requestsCounter.Add(1, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "failed"));
        _requestDurationMs.Record(durationMs, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "failed"));
    }

    public void RecordCircuitBreakerRejected(string requestName)
    {
        Interlocked.Increment(ref _rejectedByCircuitBreakerRequests);
        _requestsCounter.Add(1, new KeyValuePair<string, object?>("request", requestName), new KeyValuePair<string, object?>("status", "rejected_circuit_open"));
    }

    public void RecordCircuitOpened(string? failureReason, DateTimeOffset utcNow)
    {
        _circuitState = "Open";
        _lastCircuitStateChangedAtUtc = utcNow;
        _lastCircuitFailureReason = failureReason;
        Interlocked.Increment(ref _circuitOpenedCount);
        _circuitStateChangesCounter.Add(1, new KeyValuePair<string, object?>("state", "Open"));
    }

    public void RecordCircuitClosed(DateTimeOffset utcNow)
    {
        _circuitState = "Closed";
        _lastCircuitStateChangedAtUtc = utcNow;
        _lastCircuitFailureReason = null;
        _circuitStateChangesCounter.Add(1, new KeyValuePair<string, object?>("state", "Closed"));
    }

    public void RecordCircuitHalfOpened(DateTimeOffset utcNow)
    {
        _circuitState = "HalfOpen";
        _lastCircuitStateChangedAtUtc = utcNow;
        _circuitStateChangesCounter.Add(1, new KeyValuePair<string, object?>("state", "HalfOpen"));
    }

    public MonitoringSnapshot GetSnapshot() => new(
        CircuitState: _circuitState,
        CircuitOpenedCount: Interlocked.Read(ref _circuitOpenedCount),
        LastCircuitStateChangedAtUtc: _lastCircuitStateChangedAtUtc,
        LastCircuitFailureReason: _lastCircuitFailureReason,
        TotalRequests: Interlocked.Read(ref _totalRequests),
        SucceededRequests: Interlocked.Read(ref _succeededRequests),
        FailedRequests: Interlocked.Read(ref _failedRequests),
        CanceledRequests: Interlocked.Read(ref _canceledRequests),
        RejectedByCircuitBreakerRequests: Interlocked.Read(ref _rejectedByCircuitBreakerRequests));

    public string FormatPrometheusMetrics()
    {
        var snapshot = GetSnapshot();
        var stateNumeric = snapshot.CircuitState switch
        {
            "Closed" => 0,
            "HalfOpen" => 1,
            "Open" => 2,
            _ => 0
        };

        var sb = new StringBuilder();
        sb.AppendLine("# HELP productservice_circuit_breaker_state Current circuit breaker state (0=Closed, 1=HalfOpen, 2=Open).");
        sb.AppendLine("# TYPE productservice_circuit_breaker_state gauge");
        sb.AppendLine( string.Create(CultureInfo.InvariantCulture, $"productservice_circuit_breaker_state {stateNumeric}"));

        sb.AppendLine("# HELP productservice_circuit_breaker_opened_total Total times the circuit breaker has tripped open.");
        sb.AppendLine("# TYPE productservice_circuit_breaker_opened_total counter");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"productservice_circuit_breaker_opened_total {snapshot.CircuitOpenedCount}"));

        sb.AppendLine("# HELP productservice_requests_total Total MediatR requests processed by status.");
        sb.AppendLine("# TYPE productservice_requests_total counter");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"productservice_requests_total{{status=\"succeeded\"}} {snapshot.SucceededRequests}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"productservice_requests_total{{status=\"failed\"}} {snapshot.FailedRequests}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"productservice_requests_total{{status=\"canceled\"}} {snapshot.CanceledRequests}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"productservice_requests_total{{status=\"rejected_circuit_open\"}} {snapshot.RejectedByCircuitBreakerRequests}"));

        return sb.ToString();
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}
