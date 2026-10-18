using UserService.Application.Abstractions;

namespace UserService.Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserResponse>;
