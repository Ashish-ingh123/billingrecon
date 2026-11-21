using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The rules the validators of the refund use cases share.</summary>
internal static class RefundRules
{
    /// <summary>Above zero, at most <see cref="Refund.MaxAmount"/>, with at most two decimals.</summary>
    public static IRuleBuilderOptions<T, decimal?> RefundAmount<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(amount => amount is { } value && value > 0m && value <= Refund.MaxAmount && Money.TryOf(value, out _))
            .WithMessage($"The amount is required: more than 0 and at most 9999999999.99, with at most {Money.Scale} decimals.");

    /// <summary>One of the methods of a payment (BR-301).</summary>
    public static IRuleBuilderOptions<T, PaymentMethod?> RefundMethod<T>(this IRuleBuilder<T, PaymentMethod?> rule)
        => rule
            .Must(method => method is { } value && Enum.IsDefined(value))
            .WithMessage("The method is required: Cash, Card, UPI, BankTransfer or Cheque.");

    /// <summary>The reference of BR-301: at most 100 characters, and required unless the method is cash.</summary>
    public static IRuleBuilderOptions<T, string?> RefundReference<T>(this IRuleBuilder<T, string?> rule, Func<T, PaymentMethod?> method)
        => rule
            .OptionalText(Refund.ReferenceMaxLength, "reference")
            .Must((command, reference) => method(command) is null or PaymentMethod.Cash || !string.IsNullOrWhiteSpace(reference))
            .WithMessage("The reference is required for every method except Cash: the UPI id, card authorisation, UTR or cheque number (BR-301).");

    /// <summary>Required, at most 500 characters.</summary>
    public static IRuleBuilderOptions<T, string> RefundReason<T>(this IRuleBuilder<T, string> rule)
        => rule.RequiredText(Refund.ReasonMaxLength, "reason");

    /// <summary>The version the client read, from <c>If-Match</c>.</summary>
    public static IRuleBuilderOptions<T, string> RefundVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("The version you read is required (If-Match).");
}
