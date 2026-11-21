using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="UpdateCreditNoteHeaderCommand"/>: the version the client read, the date and the reason.</summary>
public sealed class UpdateCreditNoteHeaderCommandValidator : AbstractValidator<UpdateCreditNoteHeaderCommand>
{
    public UpdateCreditNoteHeaderCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        RuleFor(command => command.CreditNoteDate).NotNull().WithMessage("The credit note date is required.");
        RuleFor(command => command.Reason).CreditNoteReason();
    }
}
