using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Api.Endpoints;
using ProductService.Application.Exceptions;
using ProductService.Application.Products;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;
using ProductService.Domain.Products.Events;
using Testcontainers.PostgreSql;

namespace ProductService.IntegrationTests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("productdb_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => Postgres.StartAsync();

    public Task DisposeAsync() => Postgres.DisposeAsync().AsTask();
}

public sealed class ProductEndpointsIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly PublishedDomainEventsSpy _eventsSpy = new();

    private sealed class PublishedDomainEventsSpy
    {
        public ConcurrentBag<IDomainEvent> Events { get; } = [];
        public bool ThrowOnProductCreated { get; set; }
    }

    private sealed class SpyDomainEventHandler(PublishedDomainEventsSpy spy)
        : INotificationHandler<ProductCreated>,
          INotificationHandler<ProductPriceChanged>,
          INotificationHandler<ProductDeactivated>,
          INotificationHandler<ProductActivated>
    {
        public Task Handle(ProductCreated notification, CancellationToken cancellationToken)
        {
            if (spy.ThrowOnProductCreated)
            {
                throw new InvalidOperationException("Simulated post-commit event handler failure.");
            }

            spy.Events.Add(notification);
            return Task.CompletedTask;
        }

        public Task Handle(ProductPriceChanged notification, CancellationToken cancellationToken)
        {
            spy.Events.Add(notification);
            return Task.CompletedTask;
        }

        public Task Handle(ProductDeactivated notification, CancellationToken cancellationToken)
        {
            spy.Events.Add(notification);
            return Task.CompletedTask;
        }

        public Task Handle(ProductActivated notification, CancellationToken cancellationToken)
        {
            spy.Events.Add(notification);
            return Task.CompletedTask;
        }
    }

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:ProductDb", fixture.Postgres.GetConnectionString());
                builder.UseSetting("Database:SeedSampleData", "false");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(_eventsSpy);
                    services.AddTransient<INotificationHandler<ProductCreated>, SpyDomainEventHandler>();
                    services.AddTransient<INotificationHandler<ProductPriceChanged>, SpyDomainEventHandler>();
                    services.AddTransient<INotificationHandler<ProductDeactivated>, SpyDomainEventHandler>();
                    services.AddTransient<INotificationHandler<ProductActivated>, SpyDomainEventHandler>();
                });
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
    public async Task CreateAndGetProduct_Returns201_NormalizesSku_SetsETag_AndPublishesProductCreatedEventAfterCommit()
    {
        var createRequest = new CreateProductRequest(
            Name: "Mechanical Keyboard",
            Description: "Tactile switches",
            Sku: "  kb-int-001  ",
            Price: 129.99m,
            Category: "Peripherals");

        var createResponse = await _client.PostAsJsonAsync("/api/products", createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        Assert.NotNull(createResponse.Headers.ETag);
        Assert.True(createResponse.Headers.Contains("X-Correlation-Id"));

        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(created);
        Assert.Equal("KB-INT-001", created.Sku);
        Assert.Equal("Mechanical Keyboard", created.Name);
        Assert.True(created.Version > 0);

        // Verify ProductCreated domain event was published after commit
        Assert.Contains(
            _eventsSpy.Events.OfType<ProductCreated>(),
            e => e.ProductId == created.Id && e.Sku == "KB-INT-001" && e.Price == 129.99m);

        var getResponse = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getResponse.Headers.ETag);

        var fetched = await getResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("KB-INT-001", fetched.Sku);
        Assert.Equal(created.Version, fetched.Version);
    }

    [Fact]
    public async Task CreateProduct_WhenPostCommitDomainEventHandlerThrows_StillReturns201Created()
    {
        _eventsSpy.ThrowOnProductCreated = true;
        try
        {
            var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
                Name: "Resilient Product",
                Description: "Should succeed even if event handler throws after commit",
                Sku: "RES-EVT-001",
                Price: 25.00m,
                Category: "Testing"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.NotNull(created);
            Assert.Equal("RES-EVT-001", created.Sku);
        }
        finally
        {
            _eventsSpy.ThrowOnProductCreated = false;
        }
    }

    [Fact]
    public async Task CreateProduct_WithDuplicateSku_Returns409ProblemDetails()
    {
        var request = new CreateProductRequest(
            Name: "USB-C Hub",
            Description: "7-in-1 hub",
            Sku: "HUB-DUP-001",
            Price: 49.99m,
            Category: "Accessories");

        var first = await _client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await _client.PostAsJsonAsync("/api/products", request with { Sku = "  hub-dup-001 " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task CreateProduct_WithInvalidData_Returns400ValidationProblemDetails()
    {
        var invalidRequest = new CreateProductRequest(
            Name: "",
            Description: "Invalid product",
            Sku: "INV-001",
            Price: 10.999m,
            Category: "Peripherals");

        var response = await _client.PostAsJsonAsync("/api/products", invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.True(problem.Errors.ContainsKey("Name"));
        Assert.True(problem.Errors.ContainsKey("Price"));
    }

    [Fact]
    public async Task GetUpdateDeactivateActivate_WhenProductDoesNotExist_Returns404ProblemDetails()
    {
        var nonExistentId = Guid.NewGuid();

        var getResponse = await _client.GetAsync($"/api/products/{nonExistentId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/products/{nonExistentId}",
            new UpdateProductRequest("Updated", "Desc", 10m, "Cat"));
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var deactivateResponse = await _client.DeleteAsync($"/api/products/{nonExistentId}");
        Assert.Equal(HttpStatusCode.NotFound, deactivateResponse.StatusCode);

        var activateResponse = await _client.PostAsync($"/api/products/{nonExistentId}/activate", null);
        Assert.Equal(HttpStatusCode.NotFound, activateResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateDeactivateActivate_PublishesAllDomainEvents_AndUpdateWhenInactiveReturns422WithDomainCode()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Webcam 1080p",
            Description: "Full HD webcam",
            Sku: "CAM-1080",
            Price: 59.99m,
            Category: "Cameras"));
        var created = (await createResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        // 1. Update active product -> 200 OK & publishes ProductPriceChanged
        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{created.Id}", new UpdateProductRequest(
            Name: "Webcam 1080p Pro",
            Description: "Updated Full HD 60fps webcam",
            Price: 69.99m,
            Category: "Cameras",
            Version: created.Version));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = (await updateResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal("Webcam 1080p Pro", updated.Name);
        Assert.Equal(69.99m, updated.Price);
        Assert.Contains(
            _eventsSpy.Events.OfType<ProductPriceChanged>(),
            e => e.ProductId == created.Id && e.OldPrice == 59.99m && e.NewPrice == 69.99m);

        // 2. Deactivate product -> 204 No Content (and idempotent on second call) & publishes ProductDeactivated once
        var deactivate1 = await _client.DeleteAsync($"/api/products/{created.Id}");
        var deactivate2 = await _client.DeleteAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deactivate2.StatusCode);
        Assert.Single(_eventsSpy.Events.OfType<ProductDeactivated>(), e => e.ProductId == created.Id);

        // 3. GET inactive product -> 404 Not Found
        var getInactive = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getInactive.StatusCode);

        // 4. PUT on inactive product -> 422 Unprocessable Entity with code = product.inactive
        var updateInactive = await _client.PutAsJsonAsync($"/api/products/{created.Id}", new UpdateProductRequest(
            Name: "Should Fail",
            Description: "Inactive",
            Price: 79.99m,
            Category: "Cameras"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, updateInactive.StatusCode);

        using var doc = JsonDocument.Parse(await updateInactive.Content.ReadAsStringAsync());
        Assert.Equal("product.inactive", doc.RootElement.GetProperty("code").GetString());

        // 5. Activate product -> 204 No Content (and idempotent on second call) & publishes ProductActivated once
        var activate1 = await _client.PostAsync($"/api/products/{created.Id}/activate", null);
        var activate2 = await _client.PostAsync($"/api/products/{created.Id}/activate", null);
        Assert.Equal(HttpStatusCode.NoContent, activate1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, activate2.StatusCode);
        Assert.Single(_eventsSpy.Events.OfType<ProductActivated>(), e => e.ProductId == created.Id);

        // 6. GET reactivated product -> 200 OK
        var getReactivated = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getReactivated.StatusCode);
    }

    [Fact]
    public async Task UpdateProduct_WithStaleVersionOrStaleIfMatchHeader_Returns409Conflict()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "HTTP Concurrency Item",
            Description: "Initial state",
            Sku: "HTTP-CONC-001",
            Price: 50.00m,
            Category: "Testing"));
        var created = (await createResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        // First update with current version succeeds and advances PostgreSQL xmin
        var firstUpdate = await _client.PutAsJsonAsync($"/api/products/{created.Id}", new UpdateProductRequest(
            Name: "HTTP Concurrency Item v2",
            Description: "First update",
            Price: 55.00m,
            Category: "Testing",
            Version: created.Version));
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        // Second update using the stale initial version in the body must return 409 Conflict
        var staleBodyUpdate = await _client.PutAsJsonAsync($"/api/products/{created.Id}", new UpdateProductRequest(
            Name: "HTTP Concurrency Item Stale Body",
            Description: "Stale update",
            Price: 60.00m,
            Category: "Testing",
            Version: created.Version));
        Assert.Equal(HttpStatusCode.Conflict, staleBodyUpdate.StatusCode);

        // Third update using a stale If-Match header must also return 409 Conflict
        using var staleHeaderRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateProductRequest(
                Name: "HTTP Concurrency Item Stale ETag",
                Description: "Stale ETag update",
                Price: 65.00m,
                Category: "Testing"))
        };
        staleHeaderRequest.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{created.Version}\""));

        var staleHeaderUpdate = await _client.SendAsync(staleHeaderRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleHeaderUpdate.StatusCode);
    }

    [Fact]
    public async Task OptimisticConcurrency_WhenTwoScopesUpdateSameProduct_SecondSaveThrowsConflictException()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Concurrency Test Item",
            Description: "Testing PostgreSQL xmin",
            Sku: "CONC-001",
            Price: 100.00m,
            Category: "Testing"));
        var created = (await createResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IProductRepository>();
        var uow1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var repo2 = scope2.ServiceProvider.GetRequiredService<IProductRepository>();
        var uow2 = scope2.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Both scopes load the same product version (same xmin)
        var productInScope1 = (await repo1.GetByIdAsync(created.Id))!;
        var productInScope2 = (await repo2.GetByIdAsync(created.Id))!;

        productInScope1.ChangePrice(Price.Create(110.00m), DateTimeOffset.UtcNow);
        await uow1.SaveChangesAsync();

        // Scope 2 now has a stale xmin token -> saving must throw ConflictException (HTTP 409)
        productInScope2.ChangePrice(Price.Create(120.00m), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ConflictException>(() => uow2.SaveChangesAsync());
    }

    [Fact]
    public async Task ListProducts_SupportsSearchCategoryPriceRangeSortAndPagingAcrossMultiplePages()
    {
        await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Alpha Audio Headset",
            Description: "Studio headset",
            Sku: "LIST-AUD-1",
            Price: 100m,
            Category: "FilterTestAudio"));

        await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Beta Audio Headset",
            Description: "Wireless studio headset",
            Sku: "LIST-AUD-2",
            Price: 200m,
            Category: "FilterTestAudio"));

        await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Charlie Audio Headset",
            Description: "Pro studio headset",
            Sku: "LIST-AUD-3",
            Price: 150m,
            Category: "FilterTestAudio"));

        await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Name: "Gamma Monitor",
            Description: "4K display",
            Sku: "LIST-MON-4",
            Price: 400m,
            Category: "FilterTestMonitors"));

        // Sort by price descending, page 1 (pageSize=2)
        var page1ByPriceDesc = await _client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?search=headset&category=FilterTestAudio&minPrice=50&maxPrice=250&sortBy=price&sortDir=desc&page=1&pageSize=2");

        Assert.NotNull(page1ByPriceDesc);
        Assert.Equal(3, page1ByPriceDesc.TotalCount);
        Assert.Equal(2, page1ByPriceDesc.TotalPages);
        Assert.Equal(2, page1ByPriceDesc.Items.Count);
        Assert.Equal("LIST-AUD-2", page1ByPriceDesc.Items[0].Sku);
        Assert.Equal("LIST-AUD-3", page1ByPriceDesc.Items[1].Sku);

        // Page 2 (pageSize=2)
        var page2ByPriceDesc = await _client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?search=headset&category=FilterTestAudio&minPrice=50&maxPrice=250&sortBy=price&sortDir=desc&page=2&pageSize=2");

        Assert.NotNull(page2ByPriceDesc);
        Assert.Equal(3, page2ByPriceDesc.TotalCount);
        Assert.Equal(2, page2ByPriceDesc.TotalPages);
        var page2Item = Assert.Single(page2ByPriceDesc.Items);
        Assert.Equal("LIST-AUD-1", page2Item.Sku);

        // Sort by name ascending
        var sortedByNameAsc = await _client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?category=FilterTestAudio&sortBy=name&sortDir=asc&page=1&pageSize=10");

        Assert.NotNull(sortedByNameAsc);
        Assert.Equal(3, sortedByNameAsc.Items.Count);
        Assert.Equal("Alpha Audio Headset", sortedByNameAsc.Items[0].Name);
        Assert.Equal("Beta Audio Headset", sortedByNameAsc.Items[1].Name);
        Assert.Equal("Charlie Audio Headset", sortedByNameAsc.Items[2].Name);
    }

    [Fact]
    public async Task ListProducts_WhenMinPriceExceedsMaxPrice_Returns400WithMaxPriceErrorKey()
    {
        var response = await _client.GetAsync("/api/products?minPrice=200&maxPrice=50");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.True(problem.Errors.ContainsKey("MaxPrice"));
    }

    [Fact]
    public async Task HealthChecks_LiveAndReady_Return200Ok()
    {
        var live = await _client.GetAsync("/health/live");
        var ready = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task RequestTimeout_WhenOperationExceedsReadTimeoutPolicy_CancelsTokenAndReturns504ProblemDetails()
    {
        var tokenWasCanceled = false;

        await using var timeoutFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:ProductDb", fixture.Postgres.GetConnectionString());
                builder.UseSetting("Database:SeedSampleData", "false");
                builder.UseSetting("RequestTimeouts:ReadMs", "100");
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<ProductService.Application.Abstractions.IProductReadService>(
                        _ => new SlowProductReadService(onCanceled: () => tokenWasCanceled = true));
                });
            });

        using var client = timeoutFactory.CreateClient();
        var response = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(tokenWasCanceled);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(504, problem.Status);
        Assert.Equal("Request Timeout", problem.Title);
    }

    [Fact]
    public async Task CancellationToken_WhenClientCancelsRequest_AbortsInFlightOperation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.GetAsync("/api/products", cts.Token));
    }

    private sealed class SlowProductReadService(Action onCanceled)
        : ProductService.Application.Abstractions.IProductReadService
    {
        public async Task<ProductResponse?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return null;
            }
            catch (OperationCanceledException)
            {
                onCanceled();
                throw;
            }
        }

        public async Task<PagedResponse<ProductResponse>> ListActiveAsync(
            ProductService.Application.Products.Queries.ListProducts.ListProductsQuery query,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new PagedResponse<ProductResponse>([], 1, 20, 0, 0);
        }
    }
}


