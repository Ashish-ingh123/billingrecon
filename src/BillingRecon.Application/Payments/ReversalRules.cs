using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>The rules the validators of the reversal commands share.</summary>
internal static class ReversalRules
{
    /// <summary>The version the client read: required.</summary>
    public static IRuleBuilderOptions<T, string> ExpectedVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("The version you read is required (If-Match).");

    /// <summary>Why the reversal is asked for: required, of a bounded length (BR-305, BR-306).</summary>
    public static IRuleBuilderOptions<T, string> ReversalReason<T>(this IRuleBuilder<T, string> rule)
        => rule.RequiredText(Payment.ReversalReasonMaxLength, "reason");

    /// <summary>The allocation: not the empty id.</summary>
    public static IRuleBuilderOptions<T, PaymentAllocationId> AllocationId<T>(this IRuleBuilder<T, PaymentAllocationId> rule)
        => rule.Must(allocationId => allocationId.Value != Guid.Empty).WithMessage("The allocation id is required.");
}
