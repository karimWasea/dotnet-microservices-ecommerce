using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderService.Api.Endpoints;
using OrderService.Api.Exceptions;
using OrderService.Api.Health;
using OrderService.Api.Middlewares;
using OrderService.Api.Services;
using OrderService.Application;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Auth;
using OrderService.Infrastructure.Read;
using OrderService.Infrastructure.Write;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Response.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId.ToString();
        }
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Background operational services
builder.Services.AddHostedService<OrderExpirationService>();
builder.Services.AddHostedService<OutboxCleanupService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<OrderWriteDbHealthCheck>("write-db", tags: ["ready"])
    .AddCheck<OrderReadDbHealthCheck>("read-db", tags: ["ready"])
    .AddCheck<OutboxLagHealthCheck>("outbox-lag", tags: ["ready"]);

// JWT Bearer authentication with JWKS signing key resolver
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtAuthOptions>, JwksSigningKeyProvider>((bearerOptions, jwtOptionsAccessor, jwksProvider) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;
        bearerOptions.MapInboundClaims = false;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, _, _) => jwksProvider.GetSigningKeys(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "OrderService API Reference";
        options.Theme = ScalarTheme.Purple;
    });

    // Auto-migrate in development
    using var scope = app.Services.CreateScope();
    var writeDb = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var readDb = scope.ServiceProvider.GetRequiredService<OrderReadDbContext>();
    await writeDb.Database.MigrateAsync();
    await readDb.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.Response.WriteAsync(result);
    }
});

app.MapOrderEndpoints();

app.Run();

public partial class Program;
