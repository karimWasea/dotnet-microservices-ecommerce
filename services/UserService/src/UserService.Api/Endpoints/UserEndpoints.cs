using System.Security.Claims;
using MediatR;
using UserService.Application.Exceptions;
using UserService.Application.Users;
using UserService.Application.Users.Commands.ActivateUser;
using UserService.Application.Users.Commands.ChangePassword;
using UserService.Application.Users.Commands.DeactivateUser;
using UserService.Application.Users.Queries.GetCurrentUser;
using UserService.Application.Users.Queries.GetUserById;
using UserService.Application.Users.Queries.ListUsers;

namespace UserService.Api.Endpoints;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var userId = GetCurrentUserId(principal);
            var result = await sender.Send(new GetCurrentUserQuery(userId), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization("Authenticated")
        .Produces<UserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/me/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var userId = GetCurrentUserId(principal);
            var command = new ChangePasswordCommand(
                userId,
                request.CurrentPassword,
                request.NewPassword);

            await sender.Send(command, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization("Authenticated")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            string? role,
            bool? isActive,
            string? search,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var query = new ListUsersQuery(
                role,
                isActive,
                search,
                page ?? 1,
                pageSize ?? 20);

            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization("AdminOnly")
        .Produces<PagedResponse<UserResponse>>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetUserByIdQuery(id), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization("AdminOnly")
        .Produces<UserResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeactivateUserCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminOnly")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ActivateUserCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminOnly")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static Guid GetCurrentUserId(ClaimsPrincipal principal)
    {
        var subValue = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subValue, out var userId) || userId == Guid.Empty)
        {
            throw new UnauthorizedException("Invalid or missing subject claim.");
        }

        return userId;
    }
}
