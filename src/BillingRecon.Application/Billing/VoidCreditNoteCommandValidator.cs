using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="VoidCreditNoteCommand"/>: the version the client read, and a reason.</summary>
public sealed class VoidCreditNoteCommandValidator : AbstractValidator<VoidCreditNoteCommand>
{
    public VoidCreditNoteCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.Reason).CreditNoteReason();
    }
}
