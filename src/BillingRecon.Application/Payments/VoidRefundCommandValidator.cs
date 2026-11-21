using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>Voiding needs the version and a reason.</summary>
public sealed class VoidRefundCommandValidator : AbstractValidator<VoidRefundCommand>
{
    /// <summary>Creates the validator.</summary>
    public VoidRefundCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).RefundVersion();
        RuleFor(command => command.Reason).RefundReason();
    }
}
