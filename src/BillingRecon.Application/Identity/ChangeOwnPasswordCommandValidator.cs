using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The current password, and a new one of 12 to 128 characters that is not the current one (ADR 0051).</summary>
public sealed class ChangeOwnPasswordCommandValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(command => command.CurrentPassword)
            .Must(password => password is { Length: > 0 and <= UserRules.PasswordMaxLength })
            .WithMessage($"The current password is required, at most {UserRules.PasswordMaxLength} characters.");
        RuleFor(command => command.NewPassword)
            .UserPassword()
            .Must((command, password) => password != command.CurrentPassword)
            .WithMessage("The new password is not the current one.");
    }
}
