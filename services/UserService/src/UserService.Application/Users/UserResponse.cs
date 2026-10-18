using UserService.Domain.Users;

namespace UserService.Application.Users;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static UserResponse FromDomain(User user) =>
        new(
            user.Id,
            user.Email.Value,
            user.DisplayName,
            user.Role.ToString(),
            user.IsActive,
            user.LastLoginAtUtc,
            user.CreatedAtUtc,
            user.UpdatedAtUtc);
}
