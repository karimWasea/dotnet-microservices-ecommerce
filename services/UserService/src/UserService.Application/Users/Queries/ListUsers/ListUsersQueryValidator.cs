using FluentValidation;
using UserService.Domain.Users;

namespace UserService.Application.Users.Queries.ListUsers;

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) || Enum.TryParse<Role>(role, ignoreCase: true, out _))
            .WithMessage("Role must be either 'Customer' or 'Admin'.");

        RuleFor(x => x.Search)
            .MaximumLength(100).WithMessage("Search must not exceed 100 characters.");
    }
}
