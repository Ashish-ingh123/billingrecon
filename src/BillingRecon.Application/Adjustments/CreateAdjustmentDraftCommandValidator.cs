using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The shape of <see cref="CreateAdjustmentDraftCommand"/>: a bill that is not the empty id, a kind, an amount and a
/// reason. Whether the bill exists and is posted, whether the date is allowed and whether a rounding is small enough
/// are business rules.
/// </summary>
public sealed class CreateAdjustmentDraftCommandValidator : AbstractValidator<CreateAdjustmentDraftCommand>
{
    public CreateAdjustmentDraftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.BillId)
            .Must(billId => billId.Value != Guid.Empty)
            .WithMessage("The bill id is required.");
        RuleFor(command => command.Kind).RequiredKind();
        RuleFor(command => command.Amount).RequiredAmount();
        RuleFor(command => command.Reason).RequiredReason();
    }
}
