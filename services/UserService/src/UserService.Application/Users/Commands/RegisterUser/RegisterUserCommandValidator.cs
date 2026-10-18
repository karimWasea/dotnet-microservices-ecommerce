using FluentValidation;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(Email.MaxLength).WithMessage($"Email must not exceed {Email.MaxLength} characters.")
            .EmailAddress().WithMessage("Email format is invalid.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("DisplayName is required.")
            .MaximumLength(User.MaxDisplayNameLength)
            .WithMessage($"DisplayName must not exceed {User.MaxDisplayNameLength} characters.");

        RuleFor(x => x.Password)
            .ApplyPasswordRules();
    }
}
