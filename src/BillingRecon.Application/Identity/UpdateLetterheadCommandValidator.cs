using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The shape of a letterhead (ADR 0054): each text within its length, a PIN code of six digits, an e-mail address with
/// an at sign, and a GSTIN of fifteen capital letters and digits, typed in any case. Whether the GSTIN is a real
/// registration is not something the application can know.
/// </summary>
public sealed class UpdateLetterheadCommandValidator : AbstractValidator<UpdateLetterheadCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateLetterheadCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).ExpectedVersion();
        RuleFor(command => command.LegalName).OptionalText(Tenant.LetterheadLineMaxLength, "legal name");
        RuleFor(command => command.AddressLine1).OptionalText(Tenant.LetterheadLineMaxLength, "first line of the address");
        RuleFor(command => command.AddressLine2).OptionalText(Tenant.LetterheadLineMaxLength, "second line of the address");
        RuleFor(command => command.City).OptionalText(Tenant.LetterheadPlaceMaxLength, "city");
        RuleFor(command => command.State).OptionalText(Tenant.LetterheadPlaceMaxLength, "state");
        RuleFor(command => command.Signatory).OptionalText(Tenant.LetterheadLineMaxLength, "signatory line");
        RuleFor(command => command.Phone).OptionalText(Tenant.LetterheadPhoneMaxLength, "phone number");

        RuleFor(command => command.PinCode)
            .Must(pin => string.IsNullOrWhiteSpace(pin) || (pin.Trim().Length == 6 && pin.Trim().All(char.IsAsciiDigit)))
            .WithMessage("The PIN code has six digits, or is left out.");
        RuleFor(command => command.Email)
            .OptionalText(Tenant.LetterheadEmailMaxLength, "e-mail address")
            .Must(email => string.IsNullOrWhiteSpace(email) || (email.Trim().IndexOf('@', StringComparison.Ordinal) > 0 && !email.Trim().EndsWith('@')))
            .WithMessage("The e-mail address has a name, an at sign and a domain, or is left out.");
        RuleFor(command => command.Gstin)
            .Must(gstin => string.IsNullOrWhiteSpace(gstin) || Tenant.IsGstin(SettingsRules.NormalizePrefix(gstin)))
            .WithMessage($"The GSTIN has {Tenant.GstinLength} characters, each a letter or a digit, or is left out.");
    }
}
