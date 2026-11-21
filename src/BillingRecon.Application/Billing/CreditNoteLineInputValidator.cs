using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The shape of one line of a credit note (BR-211): the numbers are the ones the value objects accept, and a line
/// that names no line of the bill carries its own description, unit price and tax rate. That the discount is not more
/// than the line amount depends on the price, which may come from the bill line: the aggregate answers it, with 422.
/// </summary>
internal sealed class CreditNoteLineInputValidator : AbstractValidator<CreditNoteLineInput>
{
    public CreditNoteLineInputValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(line => line.BillLineId)
            .Must(billLineId => billLineId is null || billLineId.Value.Value != Guid.Empty)
            .WithMessage("The bill line id is not the empty id. Leave it out for a line that credits the bill as a whole.");

        RuleFor(line => line.Description)
            .Must(description => (description?.Trim().Length ?? 0) <= BillLine.DescriptionMaxLength)
            .WithMessage($"The description has at most {BillLine.DescriptionMaxLength} characters.")
            .Must((line, description) => line.BillLineId is not null || !string.IsNullOrWhiteSpace(description))
            .WithMessage("The description is required for a line without a bill line.");

        RuleFor(line => line.Quantity)
            .NotNull().WithMessage("The quantity is required.")
            .Must(quantity => quantity is { } value && value <= BillLineInputValidator.MaxQuantity && Quantity.TryOf(value, out _))
            .WithMessage($"The quantity is more than 0 and at most 999999.999, with at most {Quantity.Scale} decimals.");

        RuleFor(line => line.UnitPrice)
            .Must((line, price) => line.BillLineId is not null || price is not null)
            .WithMessage("The unit price is required for a line without a bill line.")
            .Must(price => price is not { } value || (value <= ServiceItemDetailsValidator.MaxUnitPrice && UnitPrice.TryOf(value, out _)))
            .WithMessage($"The unit price is 0 or more, with at most {UnitPrice.Scale} decimals and 15 digits before them.");

        RuleFor(line => line.DiscountAmount)
            .Must(discount => discount is not { } value || (value >= 0m && value <= BillLine.MaxLineAmount && Money.TryOf(value, out _)))
            .WithMessage($"The discount is 0 or more, with at most {Money.Scale} decimals.");

        RuleFor(line => line.TaxRatePercent)
            .Must((line, taxRate) => line.BillLineId is not null || taxRate is not null)
            .WithMessage("The tax rate is required for a line without a bill line. Send 0 for a line that is not taxed.")
            .Must(taxRate => taxRate is not { } value || Percent.TryOf(value, out _))
            .WithMessage($"The tax rate is a percentage from 0 to 100, with at most {Percent.Scale} decimals.");
    }
}
