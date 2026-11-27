using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The shape of <see cref="OpenCashierShiftCommand"/>.</summary>
public sealed class OpenCashierShiftCommandValidator : AbstractValidator<OpenCashierShiftCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenCashierShiftCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.OpeningFloat)
            .NotNull().WithMessage("The opening float is required; 0 when the drawer is empty.")
            .Must(amount => amount is { } value && value >= 0m && value <= Payment.MaxAmount && Money.TryOf(value, out _))
            .WithMessage($"The opening float is 0 or more and at most 9999999999.99, with at most {Money.Scale} decimals.");
    }
}
