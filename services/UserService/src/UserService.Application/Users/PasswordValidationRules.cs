using FluentValidation;

namespace UserService.Application.Users;

internal static class PasswordValidationRules
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(MinLength).WithMessage($"Password must be at least {MinLength} characters long.")
            .MaximumLength(MaxLength).WithMessage($"Password must not exceed {MaxLength} characters.")
            .Must(p => p is not null && p.Any(char.IsUpper))
                .WithMessage("Password must contain at least one uppercase letter.")
            .Must(p => p is not null && p.Any(char.IsLower))
                .WithMessage("Password must contain at least one lowercase letter.")
            .Must(p => p is not null && p.Any(char.IsDigit))
                .WithMessage("Password must contain at least one digit.")
            .Must(p => p is not null && p.Any(ch => !char.IsLetterOrDigit(ch)))
                .WithMessage("Password must contain at least one symbol.");
    }
}
