using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>A password of 12 to 128 characters, with the version.</summary>
public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    /// <summary>Creates the validator.</summary>
    public ResetUserPasswordCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).UserVersion();
        RuleFor(command => command.Password).UserPassword();
    }
}
