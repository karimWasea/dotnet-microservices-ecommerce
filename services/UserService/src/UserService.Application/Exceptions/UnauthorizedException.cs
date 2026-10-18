namespace UserService.Application.Exceptions;

public sealed class UnauthorizedException(string message) : Exception(message);
