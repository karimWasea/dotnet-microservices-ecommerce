using UserService.Application.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Queries.ListUsers;

public sealed class ListUsersQueryHandler(IUserReadService userReadService)
    : IQueryHandler<ListUsersQuery, PagedResponse<UserResponse>>
{
    public async Task<PagedResponse<UserResponse>> Handle(
        ListUsersQuery request,
        CancellationToken cancellationToken)
    {
        Role? parsedRole = null;
        if (!string.IsNullOrWhiteSpace(request.Role) &&
            Enum.TryParse<Role>(request.Role, ignoreCase: true, out var role))
        {
            parsedRole = role;
        }

        return await userReadService.ListAsync(
            parsedRole,
            request.IsActive,
            request.Search,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
