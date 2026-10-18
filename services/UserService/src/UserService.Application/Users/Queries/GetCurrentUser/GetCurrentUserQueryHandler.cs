using UserService.Application.Abstractions;
using UserService.Application.Exceptions;

namespace UserService.Application.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(IUserReadService userReadService)
    : IQueryHandler<GetCurrentUserQuery, UserResponse>
{
    public async Task<UserResponse> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var user = await userReadService.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException($"User with id '{request.UserId}' was not found.");

        return user;
    }
}
