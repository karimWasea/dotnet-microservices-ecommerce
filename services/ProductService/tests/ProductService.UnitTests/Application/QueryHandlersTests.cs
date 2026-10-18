using NSubstitute;
using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;
using ProductService.Application.Products;
using ProductService.Application.Products.Queries.GetProductById;
using ProductService.Application.Products.Queries.ListProducts;

namespace ProductService.UnitTests.Application;

public sealed class QueryHandlersTests
{
    private readonly IProductReadService _readService = Substitute.For<IProductReadService>();

    private static ProductResponse SampleDto(Guid id) =>
        new(
            id,
            "Keyboard",
            "Desc",
            "KB-001",
            99.99m,
            "Peripherals",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetProductByIdQueryHandler_WhenFound_ReturnsProductResponse()
    {
        var id = Guid.NewGuid();
        var expected = SampleDto(id);
        _readService.GetActiveByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(expected);

        var handler = new GetProductByIdQueryHandler(_readService);
        var actual = await handler.Handle(new GetProductByIdQuery(id), CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetProductByIdQueryHandler_WhenMissing_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        _readService.GetActiveByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((ProductResponse?)null);

        var handler = new GetProductByIdQueryHandler(_readService);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetProductByIdQuery(id), CancellationToken.None));
    }

    [Fact]
    public async Task ListProductsQueryHandler_ReturnsPagedResponseFromReadService()
    {
        var query = new ListProductsQuery(Category: "Peripherals", Page: 1, PageSize: 10);
        var expected = new PagedResponse<ProductResponse>([SampleDto(Guid.NewGuid())], 1, 10, 1, 1);

        _readService.ListActiveAsync(query, Arg.Any<CancellationToken>())
            .Returns(expected);

        var handler = new ListProductsQueryHandler(_readService);
        var actual = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(expected, actual);
    }
}
