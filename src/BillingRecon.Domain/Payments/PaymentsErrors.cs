using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The failures of the Payments module. The codes are part of the API contract (docs/api.md).</summary>
public static class PaymentsErrors
{
    public const string PaymentNotFoundCode = "payment_not_found";

    /// <summary>Code of <see cref="PaymentReferenceAlreadyRecorded"/>.</summary>
    public const string PaymentReferenceAlreadyRecordedCode = "payment_reference_already_recorded";
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
    public const string PaymentRefundedCode = "payment_refunded";
    public const string RefundNotFoundCode = "refund_not_found";
    public const string RefundNotDraftCode = "refund_not_draft";
    public const string RefundPendingApprovalCode = "refund_pending_approval";
    public const string RefundNotPendingApprovalCode = "refund_not_pending_approval";
    public const string RefundAlreadyPostedCode = "refund_already_posted";
    public const string RefundDateInFutureCode = "refund_date_in_future";
    public const string RefundDateTooOldCode = "refund_date_too_old";
    public const string RefundDateBeforeSourceDateCode = "refund_date_before_source_date";
    public const string RefundExceedsAvailableCode = "refund_exceeds_available";
    public const string OpeningAdvanceNotReversibleCode = "opening_advance_not_reversible";

    /// <summary>No payment with that id exists for the tenant. Another tenant's payment gets the same answer.</summary>
    public static Error PaymentNotFound() => Error.NotFound(PaymentNotFoundCode, "The payment does not exist.");

    /// <summary>
    /// A payment of this payer by this method with this reference is recorded already and was not reversed (BR-301 as
    /// amended 2026-11-26; review finding R2): the same receipt is not recorded twice. <paramref name="number"/> is the
    /// payment that holds the reference, when it is known.
    /// </summary>
    public static Error PaymentReferenceAlreadyRecorded(string? number)
        => Error.BusinessRule(
            PaymentReferenceAlreadyRecordedCode,
            number is null
                ? "A payment of this payer with this method and reference is recorded already. Open the payer's payments before recording it again."
                : $"This reference is recorded already for this payer, as payment {number}. If this is other money, give it its own reference; if {number} was a mistake, ask for its reversal.");

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

    /// <summary>
    /// Reversing an opening advance (ADR 0053): no money came in here, so there is none to take back out of the cash
    /// received. What the payer is owed is refunded; what was brought in by mistake is refunded with that as its reason.
    /// </summary>
    public static Error OpeningAdvanceNotReversible()
        => Error.BusinessRule(OpeningAdvanceNotReversibleCode, "An opening advance is not reversed: it was brought in at the cut-over, not received here. Refund it instead.");

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

    /// <summary>Asking to reverse a payment from which a refund was posted (BR-306): the money left already.</summary>
    public static Error PaymentRefunded()
        => Error.BusinessRule(PaymentRefundedCode, "A refund was posted from the payment, so it cannot be reversed (BR-306). Correct it with a new payment.");

    /// <summary>No refund with that id exists for the tenant. Another tenant's refund gets the same answer.</summary>
    public static Error RefundNotFound() => Error.NotFound(RefundNotFoundCode, "The refund does not exist.");

    /// <summary>Correcting, voiding or submitting a draft refund that was voided.</summary>
    public static Error RefundNotDraft()
        => Error.BusinessRule(RefundNotDraftCode, "Only a draft refund can be corrected, voided or submitted.");

    /// <summary>Correcting, voiding or submitting a refund that waits for a second person (BR-604).</summary>
    public static Error RefundPendingApproval()
        => Error.Conflict(RefundPendingApprovalCode, "The refund is waiting for approval and cannot be changed until it is approved or rejected (BR-604).");

    /// <summary>Approving or rejecting a refund that is a draft: nobody has asked for an approval.</summary>
    public static Error RefundNotPendingApproval()
        => Error.BusinessRule(RefundNotPendingApprovalCode, "The refund is not waiting for approval, so there is nothing to approve or reject.");

    /// <summary>Correcting, voiding, submitting or deciding a refund that is posted already (BR-006, BR-308).</summary>
    public static Error RefundAlreadyPosted()
        => Error.Conflict(RefundAlreadyPostedCode, "The refund is posted already and can no longer be changed (BR-006, BR-308).");

    /// <summary>The refund date is after today in India (BR-205).</summary>
    public static Error RefundDateInFuture() => Error.BusinessRule(RefundDateInFutureCode, "The refund date cannot be in the future (BR-205).");

    /// <summary>The refund date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error RefundDateTooOld(DateOnly earliest)
        => Error.BusinessRule(RefundDateTooOldCode, $"The refund date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");

    /// <summary>The refund date is before the date of the payment or credit note it comes from (BR-307).</summary>
    public static Error RefundDateBeforeSourceDate(DateOnly sourceDate)
        => Error.BusinessRule(RefundDateBeforeSourceDateCode, $"The refund date cannot be before the date of the document it comes from, {sourceDate:yyyy-MM-dd} (BR-307).");

    /// <summary>The refund is for more than its payment or credit note has left (BR-307).</summary>
    public static Error RefundExceedsAvailable(string sourceNumber, Money available)
        => Error.BusinessRule(RefundExceedsAvailableCode, $"{sourceNumber} has {available} left to refund; a refund cannot be for more (BR-307).");

    /// <summary>Code of <see cref="ShiftNotFound"/>.</summary>
    public const string ShiftNotFoundCode = "shift_not_found";

    /// <summary>Code of <see cref="ShiftNotOpen"/>.</summary>
    public const string ShiftNotOpenCode = "shift_not_open";

    /// <summary>Code of <see cref="ShiftAlreadyOpen"/>.</summary>
    public const string ShiftAlreadyOpenCode = "shift_already_open";

    /// <summary>Code of <see cref="ShiftDateMismatch"/>.</summary>
    public const string ShiftDateMismatchCode = "shift_date_mismatch";

    /// <summary>Code of <see cref="ShiftNotOwn"/>.</summary>
    public const string ShiftNotOwnCode = "shift_not_own";

    /// <summary>Code of <see cref="ShiftDifferenceNeedsReason"/>.</summary>
    public const string ShiftDifferenceNeedsReasonCode = "shift_difference_needs_reason";

    /// <summary>Code of <see cref="ShiftNotPendingApproval"/>.</summary>
    public const string ShiftNotPendingApprovalCode = "shift_not_pending_approval";

    /// <summary>No shift with that id exists for the tenant, or it is another cashier's and the user reads only their own.</summary>
    public static Error ShiftNotFound() => Error.NotFound(ShiftNotFoundCode, "The shift does not exist.");

    /// <summary>
    /// Cash was to be taken or paid out, or a shift counted, and the cashier has no open shift (BR-311).
    /// <paramref name="ofDrafter"/> says the shift asked for is that of the cashier who drafted a refund, not the caller's.
    /// </summary>
    public static Error ShiftNotOpen(bool ofDrafter = false)
        => Error.BusinessRule(
            ShiftNotOpenCode,
            ofDrafter
                ? "The cashier who drafted this refund has no open shift, and cash is paid out of an open shift (BR-311). Approve it when that shift is open, or reject it."
                : "Cash is taken and paid out in an open shift (BR-311). Open your shift first.");

    /// <summary>The user has a shift that is open or waits for approval, and opened another (BR-310).</summary>
    public static Error ShiftAlreadyOpen()
        => Error.BusinessRule(ShiftAlreadyOpenCode, "You have a shift that is open or waits for approval; a user has one shift at a time (BR-310).");

    /// <summary>A cash document is dated another day than the shift it would join (BR-311).</summary>
    public static Error ShiftDateMismatch(DateOnly shiftDate)
        => Error.BusinessRule(
            ShiftDateMismatchCode,
            $"Cash in this shift is dated {shiftDate:yyyy-MM-dd}, the business date of the shift (BR-311). Use that date, or close the shift and open a new one.");

    /// <summary>A user closed a shift that is another cashier's, or a request with no user opened one.</summary>
    public static Error ShiftNotOwn() => Error.Forbidden(ShiftNotOwnCode, "A shift is opened and closed by its own cashier.");

    /// <summary>The counted cash differs from the expected cash and no reason was given (BR-313).</summary>
    public static Error ShiftDifferenceNeedsReason(Money difference)
        => Error.BusinessRule(
            ShiftDifferenceNeedsReasonCode,
            $"The counted cash differs from the expected cash by {difference}; a difference needs a reason (BR-313).");

    /// <summary>Approving or rejecting the closing of a shift that does not wait for approval.</summary>
    public static Error ShiftNotPendingApproval()
        => Error.BusinessRule(ShiftNotPendingApprovalCode, "The shift's closing is not waiting for approval, so there is nothing to approve or reject.");
}
