using UserService.Infrastructure.Security;

namespace UserService.Api.Endpoints;

public static class JwksEndpoints
{
    public static IEndpointRouteBuilder MapJwksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", (RsaKeyMaterial rsaKeyMaterial) =>
            Results.Ok(rsaKeyMaterial.GetJwksDocument()))
            .AllowAnonymous()
            .WithTags("JWKS")
            .Produces<JwksDocument>(StatusCodes.Status200OK);

        return app;
    }
}
