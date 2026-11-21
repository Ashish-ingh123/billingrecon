using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>The rules the validators of the adjustment commands share: the kind, the amount and the reason.</summary>
internal static class AdjustmentRules
{
    /// <summary>A kind the enum defines; required.</summary>
    public static IRuleBuilderOptions<T, AdjustmentKind?> RequiredKind<T>(this IRuleBuilder<T, AdjustmentKind?> rule)
        => rule
            .Must(kind => kind is { } value && Enum.IsDefined(value))
            .WithMessage("The kind is required: Discount, WriteOff or Rounding.");

    /// <summary>An amount above zero, exact to the paisa, and not larger than an adjustment can hold; required.</summary>
    public static IRuleBuilderOptions<T, decimal?> RequiredAmount<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(amount => amount is { } value && value > 0m && value <= Adjustment.MaxAmount && Money.TryOf(value, out _))
            .WithMessage($"The amount is required: more than 0 and at most 9999999999.99, with at most {Money.Scale} decimals.");

    /// <summary>Why the bill is lowered, or why the draft is voided: required, of a bounded length.</summary>
    public static IRuleBuilderOptions<T, string> RequiredReason<T>(this IRuleBuilder<T, string> rule)
        => rule.RequiredText(Adjustment.ReasonMaxLength, "reason");
}
