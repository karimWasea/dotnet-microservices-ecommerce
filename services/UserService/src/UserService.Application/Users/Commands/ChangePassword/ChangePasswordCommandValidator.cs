using FluentValidation;

namespace UserService.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("CurrentPassword is required.");

        RuleFor(x => x.NewPassword)
            .ApplyPasswordRules()
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("NewPassword must be different from CurrentPassword.");
    }
}
