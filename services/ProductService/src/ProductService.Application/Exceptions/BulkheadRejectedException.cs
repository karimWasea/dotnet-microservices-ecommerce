namespace ProductService.Application.Exceptions;

public sealed class BulkheadRejectedException(string message) : Exception(message);
