using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="CreateCreditNoteDraftCommand"/>: a bill that is not the empty id, a reason, and lines that
/// are each valid. Whether the bill exists and is posted, and whether the date is allowed, are business rules.
/// </summary>
public sealed class CreateCreditNoteDraftCommandValidator : AbstractValidator<CreateCreditNoteDraftCommand>
{
    public CreateCreditNoteDraftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.BillId)
            .Must(billId => billId.Value != Guid.Empty)
            .WithMessage("The bill id is required.");
        RuleFor(command => command.Reason).CreditNoteReason();
        this.CreditNoteLines(command => command.Lines);
    }
}
