using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of <see cref="ResetPasswordByLinkCommand"/>.</summary>
public sealed class ResetPasswordByLinkCommandValidator : AbstractValidator<ResetPasswordByLinkCommand>
{
    /// <summary>Builds the rules.</summary>
    public ResetPasswordByLinkCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Tenant).RequiredText(Tenant.CodeMaxLength, "hospital code");
        RuleFor(command => command.Token).RequiredText(200, "link");
        RuleFor(command => command.NewPassword).UserPassword();
    }
}
