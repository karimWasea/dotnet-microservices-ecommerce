using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using UserService.Api.Endpoints;
using UserService.Application.Users;
using UserService.Infrastructure.Persistence;
using UserService.Infrastructure.Security;

namespace UserService.IntegrationTests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("userdb_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => Postgres.StartAsync();

    public Task DisposeAsync() => Postgres.DisposeAsync().AsTask();
}

public sealed class UserEndpointsIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:UserDb", fixture.Postgres.GetConnectionString());
                builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
                builder.UseSetting("RateLimiting:AuthWindowSeconds", "60");
                builder.UseSetting("Jwt:PrivateKeyPath", "");
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task HealthChecks_And_JwksEndpoint_Return200Ok()
    {
        var liveResponse = await _client.GetAsync("/health/live");
        var readyResponse = await _client.GetAsync("/health/ready");
        var jwksResponse = await _client.GetAsync("/.well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jwksResponse.StatusCode);

        var jwks = await jwksResponse.Content.ReadFromJsonAsync<JwksDocument>();
        Assert.NotNull(jwks);
        var key = Assert.Single(jwks.keys);
        Assert.Equal("RSA", key.kty);
        Assert.Equal("sig", key.use);
        Assert.Equal("RS256", key.alg);
        Assert.Equal("userservice-dev-key-1", key.kid);
        Assert.False(string.IsNullOrWhiteSpace(key.n));
        Assert.False(string.IsNullOrWhiteSpace(key.e));
    }

    [Fact]
    public async Task Register_Login_VerifyJwtViaJwks_And_GetCurrentUser_Succeeds()
    {
        var email = $"user-{Guid.NewGuid():N}@ecommerce.local";
        const string password = "Customer@123456!";

        // 1. Register
        var registerRequest = new RegisterUserRequest(email, password, "Integration User");
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.True(registerResponse.Headers.Contains("X-Correlation-Id"));

        var rawRegisterJson = await registerResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", rawRegisterJson, StringComparison.OrdinalIgnoreCase);

        var createdUser = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(createdUser);
        Assert.Equal(email, createdUser.Email);
        Assert.Equal("Customer", createdUser.Role);

        // 2. Duplicate registration returns 409 Conflict
        var duplicateResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // 3. Login
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(tokens);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(900, tokens.ExpiresInSeconds);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        // 4. Validate the JWT access token using ONLY the public key from /.well-known/jwks.json
        var jwks = await _client.GetFromJsonAsync<JwksDocument>("/.well-known/jwks.json");
        var jwk = Assert.Single(jwks!.keys);

        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(jwk.n),
            Exponent = Base64UrlEncoder.DecodeBytes(jwk.e)
        });

        var validationKey = new RsaSecurityKey(rsa) { KeyId = jwk.kid };
        var tokenHandler = new JsonWebTokenHandler();
        var tokenValidation = await tokenHandler.ValidateTokenAsync(
            tokens.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "ecommerce.userservice",
                ValidateAudience = true,
                ValidAudience = "ecommerce.api",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = validationKey,
                ValidateLifetime = true
            });

        Assert.True(tokenValidation.IsValid);
        Assert.Equal(createdUser.Id.ToString(), tokenValidation.Claims["sub"]?.ToString());

        // 5. Call GET /api/users/me without token -> 401, with token -> 200 OK
        var unauthenticatedMe = await _client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedMe.StatusCode);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var meResponse = await _client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meUser = await meResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(meUser);
        Assert.Equal(createdUser.Id, meUser.Id);
        Assert.Equal(email, meUser.Email);
    }

    [Fact]
    public async Task RefreshTokenRotation_And_ReuseDetection_RevokesAllUserTokensInDatabase()
    {
        var email = $"rotate-{Guid.NewGuid():N}@ecommerce.local";
        const string password = "Customer@123456!";

        await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, "Rotation User"));

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        var initialTokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(initialTokens);

        // 1. Legitimate rotation: RT1 -> RT2
        var refresh1Response = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(initialTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refresh1Response.StatusCode);

        var rotatedTokens = await refresh1Response.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(rotatedTokens);
        Assert.NotEqual(initialTokens.RefreshToken, rotatedTokens.RefreshToken);

        // 2. Replay already-rotated RT1 -> 401 Unauthorized AND revokes RT2 in PostgreSQL!
        var replayResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(initialTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        // 3. Now even RT2 is revoked and rejected with 401 Unauthorized!
        var rt2Response = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(rotatedTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, rt2Response.StatusCode);
    }

    [Fact]
    public async Task Login_FiveFailedAttempts_LocksOutAccountEvenWithCorrectPasswordOnSixthAttempt()
    {
        var email = $"lockout-{Guid.NewGuid():N}@ecommerce.local";
        const string correctPassword = "Customer@123456!";

        await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, correctPassword, "Lockout User"));

        // 5 failed login attempts
        for (var i = 1; i <= 5; i++)
        {
            var failedResponse = await _client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(email, "WrongPassword@123!"));
            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        // 6th attempt with the CORRECT password must still be rejected with 401 because user is locked out
        var lockedOutAttempt = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, correctPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOutAttempt.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_And_RevokeRefreshToken_WorkEndToEnd()
    {
        var email = $"pwchange-{Guid.NewGuid():N}@ecommerce.local";
        const string oldPassword = "OldPassword@123!";
        const string newPassword = "NewPassword@456!";

        await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, oldPassword, "Password User"));

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, oldPassword));
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(tokens);

        // Change password
        using var changePwRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(oldPassword, newPassword))
        };
        changePwRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var changePwResponse = await _client.SendAsync(changePwRequest);
        Assert.Equal(HttpStatusCode.NoContent, changePwResponse.StatusCode);

        // Old refresh token was revoked by password change -> 401
        var oldRefreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResponse.StatusCode);

        // Login with new password succeeds
        var newLoginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, newPassword));
        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
        var newTokens = await newLoginResponse.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(newTokens);

        // Revoke refresh token (idempotent when called twice)
        for (var i = 0; i < 2; i++)
        {
            using var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
            {
                Content = JsonContent.Create(new RevokeTokenRequest(newTokens.RefreshToken))
            };
            revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newTokens.AccessToken);
            var revokeResponse = await _client.SendAsync(revokeRequest);
            Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        }
    }

    [Fact]
    public async Task AuthorizationPolicies_CustomerGets403OnAdminEndpoints_And_AdminCanManageUsers()
    {
        var customerEmail = $"cust-{Guid.NewGuid():N}@ecommerce.local";
        const string customerPassword = "Customer@123456!";

        var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(customerEmail, customerPassword, "Managed Customer"));
        var customerUser = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(customerUser);

        var customerLogin = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(customerEmail, customerPassword));
        var customerTokens = await customerLogin.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(customerTokens);

        // 1. Customer calling GET /api/users receives 403 Forbidden
        using var customerListRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        customerListRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerTokens.AccessToken);
        var customerListResponse = await _client.SendAsync(customerListRequest);
        Assert.Equal(HttpStatusCode.Forbidden, customerListResponse.StatusCode);

        // 2. Seeded Admin logs in
        var adminLogin = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(UserDbSeeder.DefaultAdminEmail, "Admin@123456!"));
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
        var adminTokens = await adminLogin.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(adminTokens);
        Assert.Equal("Admin", adminTokens.User.Role);

        // 3. Admin lists users and gets customer by ID
        using var adminListRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/users?search={customerEmail}");
        adminListRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);
        var adminListResponse = await _client.SendAsync(adminListRequest);
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);
        var pagedUsers = await adminListResponse.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>();
        Assert.NotNull(pagedUsers);
        Assert.Contains(pagedUsers.Items, u => u.Id == customerUser.Id);

        using var adminGetByIdRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/users/{customerUser.Id}");
        adminGetByIdRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);
        var adminGetByIdResponse = await _client.SendAsync(adminGetByIdRequest);
        Assert.Equal(HttpStatusCode.OK, adminGetByIdResponse.StatusCode);

        // 4. Admin deactivates customer -> customer can no longer login
        using var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{customerUser.Id}/deactivate");
        deactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);
        var deactivateResponse = await _client.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        var loginWhileInactive = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(customerEmail, customerPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, loginWhileInactive.StatusCode);

        // 5. Admin reactivates customer -> customer can login again
        using var activateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{customerUser.Id}/activate");
        activateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);
        var activateResponse = await _client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        var loginAfterActivate = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(customerEmail, customerPassword));
        Assert.Equal(HttpStatusCode.OK, loginAfterActivate.StatusCode);
    }

    [Fact]
    public async Task RateLimiting_OnAuthEndpoints_Returns429TooManyRequestsWhenLimitExceeded()
    {
        await using var rateLimitedFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:UserDb", fixture.Postgres.GetConnectionString());
                builder.UseSetting("RateLimiting:AuthPermitLimit", "2");
                builder.UseSetting("RateLimiting:AuthWindowSeconds", "60");
                builder.UseSetting("Jwt:PrivateKeyPath", "");
            });

        using var client = rateLimitedFactory.CreateClient();

        // First 2 requests are permitted by the rate limiter (and return 400 for empty fields)
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 3rd request within the window exceeds PermitLimit=2 -> 429 Too Many Requests with ProblemDetails
        var rateLimitedResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""));
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
        Assert.True(rateLimitedResponse.Headers.Contains("Retry-After"));

        var problem = await rateLimitedResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
    }
}
