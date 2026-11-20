using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// The shape of <see cref="CreatePayerCommand"/>: a kind, a patient id that is not the empty one when there is a link,
/// and a valid name and contact details. That only self-pay payers exist is a business rule (BR-103), answered by the
/// aggregate with 422, not here.
/// </summary>
public sealed class CreatePayerCommandValidator : AbstractValidator<CreatePayerCommand>
{
    public CreatePayerCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Kind)
            .NotNull().WithMessage("The kind is required: SelfPay.")
            .IsInEnum().WithMessage("The kind is SelfPay, Insurer or Corporate.");

        RuleFor(command => command.PatientId)
            .Must(patientId => patientId is null || patientId.Value.Value != Guid.Empty)
            .WithMessage("The patient id is not the empty id. Leave it out for a guarantor.");

        Include(new PayerDetailsValidator());
    }
}
