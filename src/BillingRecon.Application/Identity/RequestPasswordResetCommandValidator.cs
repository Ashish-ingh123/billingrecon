using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of <see cref="RequestPasswordResetCommand"/>: that of a sign-in without its password.</summary>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    /// <summary>Builds the rules.</summary>
    public RequestPasswordResetCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Tenant).RequiredText(Tenant.CodeMaxLength, "hospital code");
        RuleFor(command => command.Email).RequiredText(User.EmailMaxLength, "e-mail address");
    }
}
