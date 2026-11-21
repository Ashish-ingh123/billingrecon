using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The failures of the Payments module. The codes are part of the API contract (docs/api.md).</summary>
public static class PaymentsErrors
{
    public const string PaymentNotFoundCode = "payment_not_found";
    public const string PaymentDateInFutureCode = "payment_date_in_future";
    public const string PaymentDateTooOldCode = "payment_date_too_old";
    public const string AllocationExceedsUnappliedCode = "allocation_exceeds_unapplied";
    public const string PaymentPendingReversalCode = "payment_pending_reversal";
    public const string PaymentReversedCode = "payment_reversed";
    public const string PaymentNotPendingReversalCode = "payment_not_pending_reversal";
    public const string AllocationNotFoundCode = "allocation_not_found";
    public const string AllocationPendingReversalCode = "allocation_pending_reversal";
    public const string AllocationReversedCode = "allocation_reversed";
    public const string AllocationNotPendingReversalCode = "allocation_not_pending_reversal";

    /// <summary>No payment with that id exists for the tenant. Another tenant's payment gets the same answer.</summary>
    public static Error PaymentNotFound() => Error.NotFound(PaymentNotFoundCode, "The payment does not exist.");

    /// <summary>The payment date is after today in India (BR-205).</summary>
    public static Error PaymentDateInFuture() => Error.BusinessRule(PaymentDateInFutureCode, "The payment date cannot be in the future (BR-205).");

    /// <summary>The payment date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error PaymentDateTooOld(DateOnly earliest)
        => Error.BusinessRule(PaymentDateTooOldCode, $"The payment date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");

    /// <summary>More is to be allocated than the payment has unapplied (BR-303).</summary>
    public static Error AllocationExceedsUnapplied(string number, Money unapplied)
        => Error.BusinessRule(AllocationExceedsUnappliedCode, $"Payment {number} has {unapplied} unapplied; more than that cannot be allocated (BR-303).");

    /// <summary>
    /// Allocating from, or asking again to reverse, a payment that waits for a second person's decision on its
    /// reversal (BR-306). A conflict: it is locked until that decision.
    /// </summary>
    public static Error PaymentPendingReversal()
        => Error.Conflict(PaymentPendingReversalCode, "The payment is waiting for a decision on its reversal and cannot be used until then (BR-306).");

    /// <summary>Using a payment that was reversed (BR-306): it holds no money and never changes again.</summary>
    public static Error PaymentReversed()
        => Error.Conflict(PaymentReversedCode, "The payment was reversed and can no longer be used (BR-306).");

    /// <summary>Approving or rejecting the reversal of a payment nobody asked to reverse.</summary>
    public static Error PaymentNotPendingReversal()
        => Error.BusinessRule(PaymentNotPendingReversalCode, "The payment is not waiting for a reversal, so there is nothing to approve or reject.");

    /// <summary>The payment has no allocation with that id.</summary>
    public static Error AllocationNotFound() => Error.NotFound(AllocationNotFoundCode, "The payment has no such allocation.");

    /// <summary>
    /// Asking to reverse an allocation that already waits for that decision, or to reverse a payment one of whose
    /// allocations does (BR-305): one decision at a time.
    /// </summary>
    public static Error AllocationPendingReversal()
        => Error.Conflict(AllocationPendingReversalCode, "An allocation of the payment is waiting for a decision on its reversal; decide that first (BR-305).");

    /// <summary>Reversing an allocation that was reversed already (BR-305).</summary>
    public static Error AllocationReversed()
        => Error.Conflict(AllocationReversedCode, "The allocation was reversed already (BR-305).");

    /// <summary>Approving or rejecting the reversal of an allocation nobody asked to reverse.</summary>
    public static Error AllocationNotPendingReversal()
        => Error.BusinessRule(AllocationNotPendingReversalCode, "The allocation is not waiting for a reversal, so there is nothing to approve or reject.");
}
