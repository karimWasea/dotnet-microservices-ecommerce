using NSubstitute;
using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Application.Users;
using UserService.Application.Users.Queries.GetCurrentUser;
using UserService.Application.Users.Queries.GetUserById;
using UserService.Application.Users.Queries.ListUsers;
using UserService.Domain.Users;

namespace UserService.UnitTests.Application;

public sealed class QueryHandlersTests
{
    private readonly IUserReadService _userReadService = Substitute.For<IUserReadService>();

    [Fact]
    public async Task GetCurrentUser_WhenUserExists_ReturnsUserResponse()
    {
        var userId = Guid.CreateVersion7();
        var expected = new UserResponse(
            userId,
            "alice@example.com",
            "Alice",
            "Customer",
            true,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        _userReadService.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetCurrentUserQueryHandler(_userReadService);
        var result = await handler.Handle(new GetCurrentUserQuery(userId), CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetUserById_WhenUserDoesNotExist_ThrowsNotFoundException()
    {
        var userId = Guid.CreateVersion7();
        _userReadService.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((UserResponse?)null);

        var handler = new GetUserByIdQueryHandler(_userReadService);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetUserByIdQuery(userId), CancellationToken.None));
    }

    [Fact]
    public async Task ListUsers_ParsesRoleFilterAndReturnsPagedResponse()
    {
        var expected = new PagedResponse<UserResponse>([], 1, 20, 0, 0);
        _userReadService.ListAsync(Role.Admin, true, "admin", 1, 20, Arg.Any<CancellationToken>())
            .Returns(expected);

        var handler = new ListUsersQueryHandler(_userReadService);
        var result = await handler.Handle(
            new ListUsersQuery("Admin", true, "admin", 1, 20),
            CancellationToken.None);

        Assert.Same(expected, result);
        await _userReadService.Received(1).ListAsync(
            Role.Admin,
            true,
            "admin",
            1,
            20,
            Arg.Any<CancellationToken>());
    }
}
