using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The shape of a sign-in: three values, none longer than what could ever match. The code and the address are
/// measured without the spaces around them, which the sign-in drops; the password as it is. Nothing here is about
/// whether they are right; a value of the right shape that matches nothing is refused like any other wrong sign-in.
/// </summary>
public sealed class SignInCommandValidator : AbstractValidator<SignInCommand>
{
    /// <summary>Creates the validator.</summary>
    public SignInCommandValidator()
    {
        RuleFor(command => command.Tenant)
            .Must(tenant => tenant?.Trim() is { Length: > 0 and <= Tenant.CodeMaxLength })
            .WithMessage($"The hospital code is required, at most {Tenant.CodeMaxLength} characters.");
        RuleFor(command => command.Email)
            .Must(email => email?.Trim() is { Length: > 0 and <= User.EmailMaxLength })
            .WithMessage($"The e-mail address is required, at most {User.EmailMaxLength} characters.");
        RuleFor(command => command.Password)
            .Must(password => password is { Length: > 0 and <= UserRules.PasswordMaxLength })
            .WithMessage($"The password is required, at most {UserRules.PasswordMaxLength} characters.");
    }
}
