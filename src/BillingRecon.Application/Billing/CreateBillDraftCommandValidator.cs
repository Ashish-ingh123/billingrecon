using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of <see cref="CreateBillDraftCommand"/>: a patient and a payer that are not the empty id, texts of a
/// bounded length, and lines that are each valid. Whether the patient and the payer exist and are active, whether the
/// dates are allowed today and whether a discount fits its line are business rules, answered after this.
/// </summary>
public sealed class CreateBillDraftCommandValidator : AbstractValidator<CreateBillDraftCommand>
{
    public CreateBillDraftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.PatientId)
            .Must(patientId => patientId.Value != Guid.Empty)
            .WithMessage("The patient id is required.");

        RuleFor(command => command.PayerId)
            .Must(payerId => payerId.Value != Guid.Empty)
            .WithMessage("The payer id is required.");

        RuleFor(command => command.EncounterReference).EncounterReference();
        RuleFor(command => command.Notes).Notes();

        this.Lines(command => command.Lines);
    }
}
