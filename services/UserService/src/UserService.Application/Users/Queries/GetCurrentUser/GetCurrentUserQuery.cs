using UserService.Application.Abstractions;

namespace UserService.Application.Users.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<UserResponse>;
