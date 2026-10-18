using FluentValidation;

namespace ProductService.Application.Products.Queries.ListProducts;

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    private static readonly string[] AllowedSortBy = ["name", "price", "createdat"];
    private static readonly string[] AllowedSortDir = ["asc", "desc"];

    public ListProductsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinPrice.HasValue);

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaxPrice.HasValue);

        RuleFor(x => x)
            .Must(x => !x.MinPrice.HasValue || !x.MaxPrice.HasValue || x.MinPrice.Value <= x.MaxPrice.Value)
            .OverridePropertyName("MaxPrice")
            .WithMessage("maxPrice must be greater than or equal to minPrice.");

        RuleFor(x => x.SortBy)
            .Must(s => !string.IsNullOrWhiteSpace(s) && AllowedSortBy.Contains(s.Trim().ToLowerInvariant()))
            .WithMessage("sortBy must be one of: name, price, createdAt.");

        RuleFor(x => x.SortDir)
            .Must(s => !string.IsNullOrWhiteSpace(s) && AllowedSortDir.Contains(s.Trim().ToLowerInvariant()))
            .WithMessage("sortDir must be one of: asc, desc.");
    }
}
