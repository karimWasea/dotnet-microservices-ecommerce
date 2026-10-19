namespace OrderService.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; set; }
}
