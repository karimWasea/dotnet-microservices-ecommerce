using FluentValidation;
using MediatR;
using ProductService.Application.Behaviors;
using ProductService.Application.Products.Commands.CreateProduct;
using ProductService.Application.Products.Queries.ListProducts;

namespace ProductService.UnitTests.Application;

public sealed class ValidatorsAndBehaviorTests
{
    [Fact]
    public void CreateProductCommandValidator_WithInvalidFields_ReturnsValidationErrors()
    {
        var validator = new CreateProductCommandValidator();
        var invalidCommand = new CreateProductCommand(
            Name: "",
            Description: null,
            Sku: "",
            Price: 10.999m,
            Category: "");

        var result = validator.Validate(invalidCommand);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Name));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Sku));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Price));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Category));
    }

    [Fact]
    public void ListProductsQueryValidator_WhenMinPriceExceedsMaxPriceOrInvalidSort_FailsValidation()
    {
        var validator = new ListProductsQueryValidator();
        var invalidQuery = new ListProductsQuery(
            MinPrice: 200m,
            MaxPrice: 50m,
            SortBy: "invalidColumn",
            SortDir: "sideways",
            Page: 0,
            PageSize: 500);

        var result = validator.Validate(invalidQuery);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4);
        Assert.Contains(result.Errors, e => e.PropertyName == "MaxPrice");
    }

    [Fact]
    public void CreateProductCommandValidator_WhenNameHasLeadingTrailingSpacesWithinTrimmedMaxLength_IsValid()
    {
        var validator = new CreateProductCommandValidator();
        var paddedName = "  " + new string('A', 200) + "  ";
        var command = new CreateProductCommand(
            Name: paddedName,
            Description: "Valid description",
            Sku: "  kb-trim-01  ",
            Price: 49.99m,
            Category: "  " + new string('C', 100) + "  ");

        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidationBehavior_WhenValidatorFails_ThrowsValidationExceptionAndDoesNotCallNext()
    {
        var validators = new IValidator<CreateProductCommand>[] { new CreateProductCommandValidator() };
        var behavior = new ValidationBehavior<CreateProductCommand, Guid>(validators);
        var invalidCommand = new CreateProductCommand("", null, "", -5m, "");
        var nextCalled = false;

        RequestHandlerDelegate<Guid> next = _ =>
        {
            nextCalled = true;
            return Task.FromResult(Guid.NewGuid());
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(invalidCommand, next, CancellationToken.None));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task ValidationBehavior_WhenValid_CallsNextDelegate()
    {
        var validators = new IValidator<CreateProductCommand>[] { new CreateProductCommandValidator() };
        var behavior = new ValidationBehavior<CreateProductCommand, Guid>(validators);
        var validCommand = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");
        var expectedId = Guid.NewGuid();

        RequestHandlerDelegate<Guid> next = _ => Task.FromResult(expectedId);

        var actualId = await behavior.Handle(validCommand, next, CancellationToken.None);

        Assert.Equal(expectedId, actualId);
    }

    [Fact]
    public async Task ValidationBehavior_WhenCancellationTokenIsCanceled_ThrowsOperationCanceledExceptionAndDoesNotCallNext()
    {
        var validators = new IValidator<CreateProductCommand>[] { new CreateProductCommandValidator() };
        var behavior = new ValidationBehavior<CreateProductCommand, Guid>(validators);
        var validCommand = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");
        var nextCalled = false;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        RequestHandlerDelegate<Guid> next = _ =>
        {
            nextCalled = true;
            return Task.FromResult(Guid.NewGuid());
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            behavior.Handle(validCommand, next, cts.Token));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task CircuitBreakerBehavior_WhenTransientFailuresExceedThreshold_OpensCircuitAndThrowsBrokenCircuitException()
    {
        using var metrics = new ProductService.Application.Monitoring.ProductMetrics();
        var options = new ProductService.Application.Monitoring.CircuitBreakerOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 30,
            BreakDurationSeconds = 30,
            MaxRetryAttempts = 0
        };
        using var registry = new CircuitBreakerRegistry(
            options,
            metrics,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CircuitBreakerRegistry>.Instance);
        var behavior = new CircuitBreakerBehavior<CreateProductCommand, Guid>(registry, metrics);
        var command = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");

        RequestHandlerDelegate<Guid> failingNext = _ =>
            throw new InvalidOperationException("Simulated database connection failure");

        // 2 consecutive transient failures reach MinimumThroughput = 2 & FailureRatio = 1.0 -> trips circuit Open
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(command, failingNext, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(command, failingNext, CancellationToken.None));

        Assert.Equal("Open", metrics.CircuitState);
        Assert.Equal(1, metrics.GetSnapshot().CircuitOpenedCount);

        // Next request fails fast with BrokenCircuitException without invoking the handler
        var handlerCalledWhenOpen = false;
        RequestHandlerDelegate<Guid> healthyNext = _ =>
        {
            handlerCalledWhenOpen = true;
            return Task.FromResult(Guid.NewGuid());
        };

        await Assert.ThrowsAsync<Polly.CircuitBreaker.BrokenCircuitException>(() =>
            behavior.Handle(command, healthyNext, CancellationToken.None));

        Assert.False(handlerCalledWhenOpen);
        Assert.Equal(1, metrics.GetSnapshot().RejectedByCircuitBreakerRequests);
    }

    [Fact]
    public async Task CircuitBreakerBehavior_WhenBusinessExceptionOccurs_DoesNotOpenCircuit()
    {
        using var metrics = new ProductService.Application.Monitoring.ProductMetrics();
        var options = new ProductService.Application.Monitoring.CircuitBreakerOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 30,
            BreakDurationSeconds = 30,
            MaxRetryAttempts = 2
        };
        using var registry = new CircuitBreakerRegistry(
            options,
            metrics,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CircuitBreakerRegistry>.Instance);
        var behavior = new CircuitBreakerBehavior<CreateProductCommand, Guid>(registry, metrics);
        var command = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");

        RequestHandlerDelegate<Guid> notFoundNext = _ =>
            throw new ProductService.Application.Exceptions.NotFoundException("Not found");

        await Assert.ThrowsAsync<ProductService.Application.Exceptions.NotFoundException>(() =>
            behavior.Handle(command, notFoundNext, CancellationToken.None));
        await Assert.ThrowsAsync<ProductService.Application.Exceptions.NotFoundException>(() =>
            behavior.Handle(command, notFoundNext, CancellationToken.None));

        Assert.Equal("Closed", metrics.CircuitState);
        Assert.Equal(0, metrics.GetSnapshot().TotalRetryAttempts);
    }

    [Fact]
    public async Task RetryPattern_WhenTransientFailureRecoversOnRetry_SucceedsAndRecordsRetryAttempt()
    {
        using var metrics = new ProductService.Application.Monitoring.ProductMetrics();
        var options = new ProductService.Application.Monitoring.CircuitBreakerOptions
        {
            FailureRatio = 0.9,
            MinimumThroughput = 10,
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 5,
            RetryUseJitter = false
        };
        using var registry = new CircuitBreakerRegistry(
            options,
            metrics,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CircuitBreakerRegistry>.Instance);
        var behavior = new CircuitBreakerBehavior<CreateProductCommand, Guid>(registry, metrics);
        var command = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");

        var attempts = 0;
        var expectedId = Guid.NewGuid();
        RequestHandlerDelegate<Guid> flakyNext = _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("Transient DB timeout on first attempt.");
            }

            return Task.FromResult(expectedId);
        };

        var result = await behavior.Handle(command, flakyNext, CancellationToken.None);

        Assert.Equal(expectedId, result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, metrics.GetSnapshot().TotalRetryAttempts);
    }

    [Fact]
    public async Task BulkheadPattern_WhenParallelAndQueueSlotsAreFull_ThrowsBulkheadRejectedException()
    {
        using var metrics = new ProductService.Application.Monitoring.ProductMetrics();
        var options = new ProductService.Application.Monitoring.CircuitBreakerOptions
        {
            BulkheadMaxParallelization = 1,
            BulkheadMaxQueuedActions = 1,
            MaxRetryAttempts = 0
        };
        using var registry = new CircuitBreakerRegistry(
            options,
            metrics,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CircuitBreakerRegistry>.Instance);
        var behavior = new CircuitBreakerBehavior<CreateProductCommand, Guid>(registry, metrics);
        var command = new CreateProductCommand("Keyboard", "Desc", "KB-001", 99.99m, "Peripherals");

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RequestHandlerDelegate<Guid> blockingNext = async _ =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            return Guid.NewGuid();
        };

        // 1st request occupies the active slot (BulkheadMaxParallelization = 1)
        var task1 = behavior.Handle(command, blockingNext, CancellationToken.None);
        await firstEntered.Task;

        // 2nd request occupies the single queue slot (BulkheadMaxQueuedActions = 1)
        var task2 = behavior.Handle(command, blockingNext, CancellationToken.None);
        await Task.Delay(30);

        // 3rd concurrent request exceeds active (1) + queue (1) capacity -> rejected immediately!
        await Assert.ThrowsAsync<ProductService.Application.Exceptions.BulkheadRejectedException>(() =>
            behavior.Handle(command, blockingNext, CancellationToken.None));

        Assert.Equal(1, metrics.GetSnapshot().RejectedByBulkheadRequests);

        releaseFirst.SetResult();
        await Task.WhenAll(task1, task2);
    }
}



