using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of a replaced draft: every value is given again.</summary>
public sealed class UpdateRefundCommandValidator : AbstractValidator<UpdateRefundCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateRefundCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).RefundVersion();
        RuleFor(command => command.RefundDate).NotNull().WithMessage("The refund date is required.");
        RuleFor(command => command.Amount).RefundAmount();
        RuleFor(command => command.Method).RefundMethod();
        RuleFor(command => command.Reference).RefundReference(command => command.Method);
        RuleFor(command => command.Reason).RefundReason();
    }
}
