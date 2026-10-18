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
}

