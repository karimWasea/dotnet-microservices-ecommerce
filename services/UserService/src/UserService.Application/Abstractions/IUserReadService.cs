using UserService.Application.Users;
using UserService.Domain.Users;

namespace UserService.Application.Abstractions;

public interface IUserReadService
{
    Task<UserResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResponse<UserResponse>> ListAsync(
        Role? role,
        bool? isActive,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
