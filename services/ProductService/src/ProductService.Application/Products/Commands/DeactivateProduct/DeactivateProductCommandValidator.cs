using FluentValidation;

namespace ProductService.Application.Products.Commands.DeactivateProduct;

public sealed class DeactivateProductCommandValidator : AbstractValidator<DeactivateProductCommand>
{
    public DeactivateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
