namespace OrderService.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public string Code { get; }

    public NotFoundException(string message, string code = "order.not_found")
        : base(message)
    {
        Code = code;
    }
}

public sealed class ConflictException : Exception
{
    public string Code { get; }

    public ConflictException(string message, string code = "order.conflict")
        : base(message)
    {
        Code = code;
    }
}

public sealed class ProductNotAvailableException : Exception
{
    public Guid ProductId { get; }
    public string Code => "order.product_unavailable";

    public ProductNotAvailableException(Guid productId, string message)
        : base(message)
    {
        ProductId = productId;
    }
}

public sealed class DependencyUnavailableException : Exception
{
    public string DependencyName { get; }
    public int RetryAfterSeconds { get; }
    public string Code => "dependency.unavailable";

    public DependencyUnavailableException(
        string dependencyName,
        string message,
        int retryAfterSeconds = 20,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DependencyName = dependencyName;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

public sealed class DependencyTimeoutException : Exception
{
    public string DependencyName { get; }
    public string Code => "dependency.timeout";

    public DependencyTimeoutException(
        string dependencyName,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DependencyName = dependencyName;
    }
}
