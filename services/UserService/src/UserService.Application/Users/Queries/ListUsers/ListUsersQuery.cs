using UserService.Application.Abstractions;

namespace UserService.Application.Users.Queries.ListUsers;

public sealed record ListUsersQuery(
    string? Role = null,
    bool? IsActive = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResponse<UserResponse>>;
