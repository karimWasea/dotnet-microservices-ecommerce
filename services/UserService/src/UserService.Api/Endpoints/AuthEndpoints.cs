using MediatR;
using UserService.Application.Users;
using UserService.Application.Users.Commands.Login;
using UserService.Application.Users.Commands.RefreshAccessToken;
using UserService.Application.Users.Commands.RegisterUser;
using UserService.Application.Users.Commands.RevokeRefreshToken;

namespace UserService.Api.Endpoints;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string DisplayName);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record RevokeTokenRequest(
    string RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth");

        group.MapPost("/register", async (
            RegisterUserRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Email,
                request.Password,
                request.DisplayName);

            var result = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/users/{result.Id}", result);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .Produces<UserResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", async (
            LoginRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new LoginCommand(request.Email, request.Password);
            var result = await sender.Send(command, cancellationToken);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .Produces<AuthTokensResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new RefreshAccessTokenCommand(request.RefreshToken);
            var result = await sender.Send(command, cancellationToken);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .Produces<AuthTokensResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/revoke", async (
            RevokeTokenRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new RevokeRefreshTokenCommand(request.RefreshToken);
            await sender.Send(command, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization("Authenticated")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
