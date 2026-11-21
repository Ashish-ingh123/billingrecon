using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>The failures of the Adjustments module. The codes are part of the API contract (docs/api.md).</summary>
public static class AdjustmentsErrors
{
    public const string AdjustmentNotFoundCode = "adjustment_not_found";
    public const string AdjustmentNotDraftCode = "adjustment_not_draft";
    public const string AdjustmentPendingApprovalCode = "adjustment_pending_approval";
    public const string AdjustmentNotPendingApprovalCode = "adjustment_not_pending_approval";
    public const string AdjustmentAlreadyPostedCode = "adjustment_already_posted";
    public const string AdjustmentDateInFutureCode = "adjustment_date_in_future";
    public const string AdjustmentDateTooOldCode = "adjustment_date_too_old";
    public const string AdjustmentDateBeforeBillDateCode = "adjustment_date_before_bill_date";
    public const string AdjustmentRoundingTooLargeCode = "adjustment_rounding_too_large";
    public const string AdjustmentExceedsBalanceDueCode = "adjustment_exceeds_balance_due";

    /// <summary>No adjustment with that id exists for the tenant. Another tenant's adjustment gets the same answer.</summary>
    public static Error AdjustmentNotFound() => Error.NotFound(AdjustmentNotFoundCode, "The adjustment does not exist.");

    /// <summary>Correcting, voiding or submitting a draft adjustment that was voided.</summary>
    public static Error AdjustmentNotDraft()
        => Error.BusinessRule(AdjustmentNotDraftCode, "Only a draft adjustment can be corrected, voided or submitted.");

    /// <summary>Correcting, voiding or submitting an adjustment that waits for a second person (BR-502).</summary>
    public static Error AdjustmentPendingApproval()
        => Error.Conflict(AdjustmentPendingApprovalCode, "The adjustment is waiting for approval and cannot be changed until it is approved or rejected (BR-502).");

    /// <summary>Approving or rejecting an adjustment that is a draft: nobody has asked for an approval.</summary>
    public static Error AdjustmentNotPendingApproval()
        => Error.BusinessRule(AdjustmentNotPendingApprovalCode, "The adjustment is not waiting for approval, so there is nothing to approve or reject.");

    /// <summary>Correcting, voiding, submitting or deciding an adjustment that is posted already (BR-006).</summary>
    public static Error AdjustmentAlreadyPosted()
        => Error.Conflict(AdjustmentAlreadyPostedCode, "The adjustment is posted already and can no longer be changed (BR-006).");

    /// <summary>The adjustment date is after today in India (BR-205).</summary>
    public static Error AdjustmentDateInFuture()
        => Error.BusinessRule(AdjustmentDateInFutureCode, "The adjustment date cannot be in the future (BR-205).");

    /// <summary>The adjustment date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error AdjustmentDateTooOld(DateOnly earliest)
        => Error.BusinessRule(AdjustmentDateTooOldCode, $"The adjustment date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");

    /// <summary>The adjustment date is before the date of the bill it is against (BR-501).</summary>
    public static Error AdjustmentDateBeforeBillDate(DateOnly billDate)
        => Error.BusinessRule(AdjustmentDateBeforeBillDateCode, $"The adjustment date cannot be before the bill date, {billDate:yyyy-MM-dd} (BR-501).");

    /// <summary>A rounding adjustment of one rupee or more (BR-501).</summary>
    public static Error AdjustmentRoundingTooLarge()
        => Error.BusinessRule(AdjustmentRoundingTooLargeCode, "A rounding adjustment is less than 1.00; use a discount or a write-off for more (BR-501).");

    /// <summary>The adjustment is for more than is still owed on the bill (BR-501).</summary>
    public static Error AdjustmentExceedsBalanceDue(string billNumber, Money balanceDue)
        => Error.BusinessRule(AdjustmentExceedsBalanceDueCode, $"Bill {billNumber} has a balance due of {balanceDue}; an adjustment cannot be for more (BR-501).");
}
