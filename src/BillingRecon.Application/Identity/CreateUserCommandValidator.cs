using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of a new user: an address, a name, a password of 12 to 128 characters, at least one role.</summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>Creates the validator.</summary>
    public CreateUserCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Email).UserEmail();
        RuleFor(command => command.DisplayName).UserDisplayName();
        RuleFor(command => command.Password).UserPassword();
        RuleFor(command => command.Roles).UserRoles();
    }
}
