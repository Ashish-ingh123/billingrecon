using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>The rules for the name and contact details of a payer, shared by creating and correcting one.</summary>
internal sealed class PayerDetailsValidator : AbstractValidator<IPayerDetailsInput>
{
    public PayerDetailsValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(input => input.Name).RequiredText(Payer.NameMaxLength, "name");
        RuleFor(input => input.Phone).ContactPhone(Payer.PhoneMaxLength);
        RuleFor(input => input.Email).ContactEmail(Payer.EmailMaxLength);
    }
}
