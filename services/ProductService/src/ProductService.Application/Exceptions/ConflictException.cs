namespace ProductService.Application.Exceptions;

public sealed class ConflictException(string message) : Exception(message);
