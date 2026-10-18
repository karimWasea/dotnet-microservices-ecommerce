using NSubstitute;
using ProductService.Application.Exceptions;
using ProductService.Application.Products.Commands.ActivateProduct;
using ProductService.Application.Products.Commands.CreateProduct;
using ProductService.Application.Products.Commands.DeactivateProduct;
using ProductService.Application.Products.Commands.UpdateProduct;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.UnitTests.Application;

public sealed class CommandHandlersTests
{
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private static Product CreateExistingProduct() =>
        Product.Create(
            "Mechanical Keyboard",
            "Wireless RGB",
            Sku.Create("KB-001"),
            Price.Create(129.99m),
            "Peripherals",
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task CreateProductCommandHandler_WhenSkuIsUnique_AddsProductAndSavesChanges()
    {
        var handler = new CreateProductCommandHandler(_repository, _unitOfWork, _timeProvider);
        var command = new CreateProductCommand("Mouse", "Wireless", "ms-01", 49.99m, "Peripherals");

        _repository.ExistsBySkuAsync(Sku.Create("MS-01"), Arg.Any<CancellationToken>())
            .Returns(false);

        var id = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        _repository.Received(1).Add(Arg.Is<Product>(p => p.Id == id && p.Sku.Value == "MS-01"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProductCommandHandler_WhenSkuExists_ThrowsConflictException()
    {
        var handler = new CreateProductCommandHandler(_repository, _unitOfWork, _timeProvider);
        var command = new CreateProductCommand("Mouse", "Wireless", "ms-01", 49.99m, "Peripherals");

        _repository.ExistsBySkuAsync(Sku.Create("MS-01"), Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(command, CancellationToken.None));

        _repository.DidNotReceive().Add(Arg.Any<Product>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProductCommandHandler_WhenProductExists_UpdatesDetailsAndPriceAndSaves()
    {
        var product = CreateExistingProduct();
        _repository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        var handler = new UpdateProductCommandHandler(_repository, _unitOfWork, _timeProvider);
        var command = new UpdateProductCommand(product.Id, "Updated Name", "Updated Desc", 159.99m, "Peripherals");

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Updated Name", product.Name);
        Assert.Equal(159.99m, product.Price.Amount);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProductCommandHandler_WhenProductNotFound_ThrowsNotFoundException()
    {
        var missingId = Guid.NewGuid();
        _repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var handler = new UpdateProductCommandHandler(_repository, _unitOfWork, _timeProvider);
        var command = new UpdateProductCommand(missingId, "Name", "Desc", 50m, "Cat");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProductCommandHandler_WhenVersionMismatches_ThrowsConflictException()
    {
        var product = CreateExistingProduct();
        _repository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        var handler = new UpdateProductCommandHandler(_repository, _unitOfWork, _timeProvider);
        var staleCommand = new UpdateProductCommand(
            product.Id,
            "Updated Name",
            "Updated Desc",
            159.99m,
            "Peripherals",
            Version: 999u);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(staleCommand, CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateProductCommandHandler_WhenProductExists_DeactivatesAndSaves()
    {
        var product = CreateExistingProduct();
        _repository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        var handler = new DeactivateProductCommandHandler(_repository, _unitOfWork, _timeProvider);

        await handler.Handle(new DeactivateProductCommand(product.Id), CancellationToken.None);

        Assert.False(product.IsActive);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateProductCommandHandler_WhenProductNotFound_ThrowsNotFoundException()
    {
        var missingId = Guid.NewGuid();
        _repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var handler = new DeactivateProductCommandHandler(_repository, _unitOfWork, _timeProvider);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeactivateProductCommand(missingId), CancellationToken.None));
    }

    [Fact]
    public async Task ActivateProductCommandHandler_WhenProductExists_ActivatesAndSaves()
    {
        var product = CreateExistingProduct();
        product.Deactivate(DateTimeOffset.UtcNow);
        _repository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        var handler = new ActivateProductCommandHandler(_repository, _unitOfWork, _timeProvider);

        await handler.Handle(new ActivateProductCommand(product.Id), CancellationToken.None);

        Assert.True(product.IsActive);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateProductCommandHandler_WhenProductNotFound_ThrowsNotFoundException()
    {
        var missingId = Guid.NewGuid();
        _repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var handler = new ActivateProductCommandHandler(_repository, _unitOfWork, _timeProvider);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ActivateProductCommand(missingId), CancellationToken.None));
    }
}
