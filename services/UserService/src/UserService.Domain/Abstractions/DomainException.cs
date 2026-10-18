namespace UserService.Domain.Abstractions;

public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string message, string code)
        : base(message)
    {
        Code = code;
    }
}
