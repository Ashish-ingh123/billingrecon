using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// The rules for the demographics of a patient, shared by registering and correcting one. The validator of each command
/// includes it. The date of birth is checked against the business date in India, read from the clock.
/// </summary>
internal sealed class PatientDetailsValidator : AbstractValidator<IPatientDetailsInput>
{
    private static readonly DateOnly EarliestDateOfBirth = new(1900, 1, 1);

    public PatientDetailsValidator(IClock clock)
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(input => input.FullName).RequiredText(Patient.FullNameMaxLength, "full name");

        RuleFor(input => input.DateOfBirth)
            .Must(date => date is null || (date >= EarliestDateOfBirth && date <= clock.TodayIst))
            .WithMessage("The date of birth is not in the future and not before 1 January 1900.");

        RuleFor(input => input.Sex).IsInEnum().WithMessage("The sex is Male, Female, Other or Unknown.");
        RuleFor(input => input.Phone).ContactPhone(Patient.PhoneMaxLength);
        RuleFor(input => input.Email).ContactEmail(Patient.EmailMaxLength);
        RuleFor(input => input.AddressLine1).OptionalText(Patient.AddressLineMaxLength, "first address line");
        RuleFor(input => input.AddressLine2).OptionalText(Patient.AddressLineMaxLength, "second address line");
        RuleFor(input => input.City).OptionalText(Patient.CityMaxLength, "city");
        RuleFor(input => input.State).OptionalText(Patient.StateMaxLength, "state");
        RuleFor(input => input.PinCode).PostalCode(Patient.PinCodeMaxLength);
    }
}
