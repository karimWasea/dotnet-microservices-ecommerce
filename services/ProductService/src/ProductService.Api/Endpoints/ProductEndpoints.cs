using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ProductService.Application.Products;
using ProductService.Application.Products.Commands.ActivateProduct;
using ProductService.Application.Products.Commands.CreateProduct;
using ProductService.Application.Products.Commands.DeactivateProduct;
using ProductService.Application.Products.Commands.UpdateProduct;
using ProductService.Application.Products.Queries.GetProductById;
using ProductService.Application.Products.Queries.ListProducts;

namespace ProductService.Api.Endpoints;

public sealed record CreateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    string Category);

public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    decimal Price,
    string Category,
    uint? Version = null);

public static class ProductEndpoints
{
    public const string ReadTimeoutPolicy = "ReadPolicy";
    public const string WriteTimeoutPolicy = "WritePolicy";

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products");

        group.MapPost("/", CreateProductAsync)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .WithRequestTimeout(WriteTimeoutPolicy)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/", ListProductsAsync)
            .WithName("ListProducts")
            .WithSummary("List active products with filtering, sorting, and pagination")
            .WithRequestTimeout(ReadTimeoutPolicy)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/{id:guid}", GetProductByIdAsync)
            .WithName("GetProductById")
            .WithSummary("Get an active product by ID")
            .WithRequestTimeout(ReadTimeoutPolicy)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPut("/{id:guid}", UpdateProductAsync)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product's details and price")
            .WithRequestTimeout(WriteTimeoutPolicy)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapDelete("/{id:guid}", DeactivateProductAsync)
            .WithName("DeactivateProduct")
            .WithSummary("Deactivate a product (soft delete, idempotent)")
            .WithRequestTimeout(WriteTimeoutPolicy)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPost("/{id:guid}/activate", ActivateProductAsync)
            .WithName("ActivateProduct")
            .WithSummary("Activate a product (idempotent)")
            .WithRequestTimeout(WriteTimeoutPolicy)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Created<ProductResponse>> CreateProductAsync(
        CreateProductRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(
            request.Name,
            request.Description,
            request.Sku,
            request.Price,
            request.Category);

        var productId = await sender.Send(command, cancellationToken);
        var product = await sender.Send(new GetProductByIdQuery(productId), cancellationToken);

        httpContext.Response.Headers.ETag = $"\"{product.Version}\"";
        return TypedResults.Created($"/api/products/{product.Id}", product);
    }

    private static async Task<Ok<PagedResponse<ProductResponse>>> ListProductsAsync(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new ListProductsQuery(
            Search: search,
            Category: category,
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            SortBy: sortBy ?? "createdAt",
            SortDir: sortDir ?? "desc",
            Page: page ?? 1,
            PageSize: pageSize ?? 20);

        var response = await sender.Send(query, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<ProductResponse>> GetProductByIdAsync(
        Guid id,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var product = await sender.Send(new GetProductByIdQuery(id), cancellationToken);
        httpContext.Response.Headers.ETag = $"\"{product.Version}\"";
        return TypedResults.Ok(product);
    }

    private static async Task<Ok<ProductResponse>> UpdateProductAsync(
        Guid id,
        UpdateProductRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var version = request.Version ?? TryParseIfMatchVersion(httpContext.Request.Headers.IfMatch.FirstOrDefault());

        var command = new UpdateProductCommand(
            id,
            request.Name,
            request.Description,
            request.Price,
            request.Category,
            version);

        await sender.Send(command, cancellationToken);
        var updated = await sender.Send(new GetProductByIdQuery(id), cancellationToken);

        httpContext.Response.Headers.ETag = $"\"{updated.Version}\"";
        return TypedResults.Ok(updated);
    }

    private static uint? TryParseIfMatchVersion(string? ifMatchHeader)
    {
        if (string.IsNullOrWhiteSpace(ifMatchHeader))
        {
            return null;
        }

        var trimmed = ifMatchHeader.Trim();
        if (trimmed.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..].Trim();
        }

        trimmed = trimmed.Trim('"');
        return uint.TryParse(trimmed, out var parsed) ? parsed : null;
    }

    private static async Task<NoContent> DeactivateProductAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateProductCommand(id), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ActivateProductAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ActivateProductCommand(id), cancellationToken);
        return TypedResults.NoContent();
    }
}
