using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>At least one role, with the version.</summary>
public sealed class ReplaceUserRolesCommandValidator : AbstractValidator<ReplaceUserRolesCommand>
{
    /// <summary>Creates the validator.</summary>
    public ReplaceUserRolesCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).UserVersion();
        RuleFor(command => command.Roles).UserRoles();
    }
}
