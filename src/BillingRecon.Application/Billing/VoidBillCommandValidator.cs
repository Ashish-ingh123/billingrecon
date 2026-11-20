using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="VoidBillCommand"/>: the version the client read, and a reason (US-23).</summary>
public sealed class VoidBillCommandValidator : AbstractValidator<VoidBillCommand>
{
    public VoidBillCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.Reason).RequiredText(Bill.VoidReasonMaxLength, "reason");
    }
}
