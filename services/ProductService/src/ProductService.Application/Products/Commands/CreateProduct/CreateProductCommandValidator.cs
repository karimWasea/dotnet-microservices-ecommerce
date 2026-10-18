using FluentValidation;
using ProductService.Domain.Products;

namespace ProductService.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Must(s => s is not null && s.Trim().Length <= Product.MaxNameLength)
            .WithMessage($"Name must not exceed {Product.MaxNameLength} characters.");

        RuleFor(x => x.Description)
            .MaximumLength(Product.MaxDescriptionLength)
            .When(x => x.Description is not null);

        RuleFor(x => x.Sku)
            .NotEmpty()
            .Must(s => s is not null && s.Trim().Length <= Sku.MaxLength)
            .WithMessage($"Sku must not exceed {Sku.MaxLength} characters.");

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .Must(p => decimal.Round(p, 2) == p)
            .WithMessage("Price must have at most 2 decimal places.");

        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(s => s is not null && s.Trim().Length <= Product.MaxCategoryLength)
            .WithMessage($"Category must not exceed {Product.MaxCategoryLength} characters.");
    }
}
