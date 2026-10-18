using UserService.Application.Abstractions;
using UserService.Application.Exceptions;

namespace UserService.Application.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler(IUserReadService userReadService)
    : IQueryHandler<GetUserByIdQuery, UserResponse>
{
    public async Task<UserResponse> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var user = await userReadService.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"User with id '{request.Id}' was not found.");

        return user;
    }
}
