using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OrderService.Api.Endpoints;
using OrderService.Application.Abstractions;
using OrderService.Application.IntegrationEvents.Outgoing;
using OrderService.Application.Orders.Commands.PlaceOrder;
using OrderService.Application.Orders.DTOs;
using OrderService.Infrastructure.Auth;
using OrderService.Infrastructure.Read;
using OrderService.Infrastructure.Read.Projections;
using OrderService.Infrastructure.Write;
using Testcontainers.PostgreSql;

namespace OrderService.IntegrationTests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("orderdb_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => Postgres.StartAsync();

    public Task DisposeAsync() => Postgres.DisposeAsync().AsTask();
}

public sealed class OrderEndpointsIntegrationTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly RSA _rsa = RSA.Create(2048);

    public OrderEndpointsIntegrationTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        var connStr = _fixture.Postgres.GetConnectionString();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:OrderWriteDb", connStr);
                builder.UseSetting("ConnectionStrings:OrderReadDb", connStr);
                builder.UseSetting("RabbitMq:Enabled", "false");
                builder.UseSetting("OrderExpiration:Enabled", "false");

                builder.ConfigureServices(services =>
                {
                    // Register a fake product catalog client
                    services.AddScoped<IProductCatalogClient, FakeProductCatalogClient>();

                    // Configure JWKS provider with in-memory RSA key
                    var sp = services.BuildServiceProvider();
                    var jwksProvider = sp.GetRequiredService<JwksSigningKeyProvider>();
                    var key = new RsaSecurityKey(_rsa) { KeyId = "test-key-id" };
                    jwksProvider.SeedSigningKeysForTesting([key]);
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _rsa.Dispose();
    }

    [Fact]
    public async Task HealthChecks_LiveAndReady_Return200Ok()
    {
        var liveResponse = await _client.GetAsync("/health/live");
        var readyResponse = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_WithoutJwt_Returns401Unauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                shippingAddress = new { recipientName = "Alice", street = "123 St", city = "Cairo", country = "EG" },
                items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", "test-key-1");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_WithoutIdempotencyKey_Returns400BadRequest()
    {
        var token = GenerateToken(Guid.NewGuid(), "Customer");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                shippingAddress = new { recipientName = "Alice", street = "123 St", city = "Cairo", country = "EG" },
                items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_ValidRequest_CreatesOrderAndOutboxMessageAtomically_AndReplaysOnDuplicate()
    {
        var userId = Guid.NewGuid();
        var token = GenerateToken(userId, "Customer");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var placeRequest = new OrderEndpoints.PlaceOrderRequest(
            new AddressDto("Alice Customer", "123 Nile Corniche", "Cairo", "EG"),
            [new PlaceOrderItemDto(Guid.NewGuid(), 2)],
            "USD");

        // 1. First request creates order
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(placeRequest)
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request1.Headers.Add("Idempotency-Key", idempotencyKey);

        var response1 = await _client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        var order1 = await response1.Content.ReadFromJsonAsync<OrderDetailsDto>();
        Assert.NotNull(order1);
        Assert.Equal("Pending", order1.Status);
        Assert.Equal(200.00m, order1.Total); // 2 * $100 from FakeProductCatalogClient

        // Verify outbox message committed atomically in write DB
        using (var scope = _factory.Services.CreateScope())
        {
            var writeDb = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var outboxMsg = await writeDb.OutboxMessages
                .FirstOrDefaultAsync(m => m.Type == "order.placed");

            Assert.NotNull(outboxMsg);
            Assert.Contains(order1.Id.ToString(), outboxMsg.Payload);
            Assert.Null(outboxMsg.ProcessedOnUtc);
        }

        // 2. Second request with same Idempotency-Key returns cached 200 OK replay
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(placeRequest)
        };
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request2.Headers.Add("Idempotency-Key", idempotencyKey);

        var response2 = await _client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(response2.Headers.Contains("X-Idempotency-Replayed"));

        var order2 = await response2.Content.ReadFromJsonAsync<OrderDetailsDto>();
        Assert.NotNull(order2);
        Assert.Equal(order1.Id, order2.Id);
    }

    [Fact]
    public async Task ReadModelProjector_ProjectsPlacedAndTransitionsIdempotently()
    {
        using var scope = _factory.Services.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<OrderReadModelProjector>();
        var readService = scope.ServiceProvider.GetRequiredService<IOrderReadService>();

        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var placedAt = DateTimeOffset.UtcNow;

        var placedEvent = new OrderPlacedIntegrationEvent(
            orderId,
            userId,
            1,
            "Pending",
            [new OrderLineIntegrationDto(Guid.NewGuid(), "Keyboard", 100m, 1, 100m)],
            new AddressIntegrationDto("Bob", "456 St", "Alexandria", "EG"),
            100m,
            "USD",
            placedAt);

        // Project placed
        await projector.ProjectOrderPlacedAsync(placedEvent);

        var details = await readService.GetByIdAsync(orderId);
        Assert.NotNull(details);
        Assert.Equal("Pending", details.Status);
        Assert.Equal(1, details.Version);

        // Project stock reserved
        var reservedEvent = new OrderStockReservedIntegrationEvent(orderId, userId, 2, placedAt.AddSeconds(1));
        await projector.ProjectOrderStockReservedAsync(reservedEvent);

        details = await readService.GetByIdAsync(orderId);
        Assert.NotNull(details);
        Assert.Equal("StockReserved", details.Status);
        Assert.Equal(2, details.Version);

        // Re-projecting same or older version is idempotent no-op
        await projector.ProjectOrderStockReservedAsync(reservedEvent);
        details = await readService.GetByIdAsync(orderId);
        Assert.Equal(2, details!.Version);
    }

    private string GenerateToken(Guid userId, string role)
    {
        var key = new RsaSecurityKey(_rsa) { KeyId = "test-key-id" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: "ecommerce-user-service",
            audience: "ecommerce-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakeProductCatalogClient : IProductCatalogClient
    {
        public Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default)
        {
            return Task.FromResult<ProductSnapshot?>(new ProductSnapshot(
                productId,
                "Test Product",
                100.00m,
                "USD",
                true));
        }
    }
}
