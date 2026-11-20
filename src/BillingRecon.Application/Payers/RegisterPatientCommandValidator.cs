using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The shape of <see cref="RegisterPatientCommand"/>: an MRN and valid demographics. Whether the MRN is free is the handler's question (BR-101).</summary>
public sealed class RegisterPatientCommandValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientCommandValidator(IClock clock)
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.Mrn)
            .Must(mrn => Mrn.TryOf(mrn, out _))
            .WithMessage($"The MRN is required: 1 to {Mrn.MaxLength} characters, without control characters.");

        Include(new PatientDetailsValidator(clock));
    }
}
