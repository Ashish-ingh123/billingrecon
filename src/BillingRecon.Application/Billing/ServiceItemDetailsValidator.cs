using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The rules for the name, category, price and tax rate of a service item, shared by creating and correcting one. The
/// numbers are checked by the value objects themselves (BR-201), so the validator and the domain can never disagree.
/// </summary>
internal sealed class ServiceItemDetailsValidator : AbstractValidator<IServiceItemDetailsInput>
{
    /// <summary>
    /// The largest unit price a request may carry: what a <c>decimal(19,4)</c> column holds (docs/database.md, section
    /// 1). SQL Server would refuse a larger number as an arithmetic overflow, which is a fault; here it is a field error.
    /// </summary>
    internal const decimal MaxUnitPrice = 999_999_999_999_999.9999m;

    public ServiceItemDetailsValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(input => input.Name).RequiredText(ServiceItem.NameMaxLength, "name");
        RuleFor(input => input.Category).OptionalText(ServiceItem.CategoryMaxLength, "category");

        RuleFor(input => input.DefaultUnitPrice)
            .NotNull().WithMessage("The default unit price is required. Send 0 for a service that is free.")
            .Must(price => price is { } value && value <= MaxUnitPrice && UnitPrice.TryOf(value, out _))
            .WithMessage($"The default unit price is 0 or more, with at most {UnitPrice.Scale} decimals and 15 digits before them.");

        RuleFor(input => input.TaxRatePercent)
            .NotNull().WithMessage("The tax rate is required. Send 0 for a service that is not taxed.")
            .Must(taxRate => taxRate is { } value && Percent.TryOf(value, out _))
            .WithMessage($"The tax rate is a percentage from 0 to 100, with at most {Percent.Scale} decimals.");

        RuleFor(input => input.SacCode)
            .Must(code => string.IsNullOrWhiteSpace(code) || ServiceItem.IsSacCode(code.Trim()))
            .WithMessage($"The SAC code has {ServiceItem.SacCodeLength} digits, or is left out.");
    }
}
