using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>Both values are given again, with the version.</summary>
public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateUserCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).UserVersion();
        RuleFor(command => command.Email).UserEmail();
        RuleFor(command => command.DisplayName).UserDisplayName();
    }
}
