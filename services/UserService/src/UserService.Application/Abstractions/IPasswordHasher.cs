using UserService.Domain.Users;

namespace UserService.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string rawPassword);
    bool Verify(string rawPassword, PasswordHash passwordHash);
}
