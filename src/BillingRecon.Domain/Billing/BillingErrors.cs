using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The failures of the Billing module. The codes are part of the API contract (docs/api.md).</summary>
public static class BillingErrors
{
    public const string ServiceItemNotFoundCode = "service_item_not_found";
    public const string ServiceItemCodeDuplicateCode = "service_item_code_duplicate";
    public const string ServiceItemAlreadyInactiveCode = "service_item_already_inactive";
    public const string ServiceItemAlreadyActiveCode = "service_item_already_active";
    public const string ServiceItemInactiveCode = "service_item_inactive";
    public const string BillNotFoundCode = "bill_not_found";
    public const string BillNotDraftCode = "bill_not_draft";
    public const string BillDateInFutureCode = "bill_date_in_future";
    public const string BillDateTooOldCode = "bill_date_too_old";
    public const string BillDueDateBeforeBillDateCode = "bill_due_date_before_bill_date";
    public const string BillLineDiscountExceedsAmountCode = "bill_line_discount_exceeds_amount";
    public const string BillLineAmountTooLargeCode = "bill_line_amount_too_large";
    public const string BillHasNoLinesCode = "bill_has_no_lines";
    public const string BillPendingApprovalCode = "bill_pending_approval";
    public const string BillNotPendingApprovalCode = "bill_not_pending_approval";
    public const string BillAlreadyPostedCode = "bill_already_posted";

    /// <summary>No service item with that id exists for the tenant. Another tenant's item gets the same answer.</summary>
    public static Error ServiceItemNotFound() => Error.NotFound(ServiceItemNotFoundCode, "The service item does not exist.");

    /// <summary>The tenant's catalogue already has an item with that code, active or not.</summary>
    public static Error ServiceItemCodeDuplicate()
        => Error.Conflict(ServiceItemCodeDuplicateCode, "A service item with this code is already in the catalogue.");

    /// <summary>Deactivating a service item that is already inactive.</summary>
    public static Error ServiceItemAlreadyInactive()
        => Error.BusinessRule(ServiceItemAlreadyInactiveCode, "The service item is already inactive.");

    /// <summary>Reactivating a service item that is already active.</summary>
    public static Error ServiceItemAlreadyActive()
        => Error.BusinessRule(ServiceItemAlreadyActiveCode, "The service item is already active.");

    /// <summary>A bill line names a service item that is inactive and was not already on the bill.</summary>
    public static Error ServiceItemInactive()
        => Error.BusinessRule(ServiceItemInactiveCode, "The service item is inactive, so it cannot be put on a bill line. Reactivate it, or enter the line as free text.");

    /// <summary>No bill with that id exists for the tenant. Another tenant's bill gets the same answer.</summary>
    public static Error BillNotFound() => Error.NotFound(BillNotFoundCode, "The bill does not exist.");

    /// <summary>Editing, voiding or posting a draft that was voided (BR-204). A posted bill answers <see cref="BillAlreadyPosted"/>.</summary>
    public static Error BillNotDraft() => Error.BusinessRule(BillNotDraftCode, "Only a draft bill can be edited, voided or posted (BR-204).");

    /// <summary>The bill date is after today in India (BR-205).</summary>
    public static Error BillDateInFuture() => Error.BusinessRule(BillDateInFutureCode, "The bill date cannot be in the future (BR-205).");

    /// <summary>The bill date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error BillDateTooOld(DateOnly earliest)
        => Error.BusinessRule(BillDateTooOldCode, $"The bill date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");

    /// <summary>The due date is before the bill date (BR-205).</summary>
    public static Error BillDueDateBeforeBillDate()
        => Error.BusinessRule(BillDueDateBeforeBillDateCode, "The due date cannot be before the bill date (BR-205).");

    /// <summary>The discount of line <paramref name="lineNo"/> is more than its amount (BR-201).</summary>
    public static Error BillLineDiscountExceedsAmount(int lineNo)
        => Error.BusinessRule(BillLineDiscountExceedsAmountCode, $"The discount of line {lineNo} is more than the amount of the line (BR-201).");

    /// <summary>The quantity times the unit price of line <paramref name="lineNo"/> is more than a bill line can hold.</summary>
    public static Error BillLineAmountTooLarge(int lineNo)
        => Error.BusinessRule(BillLineAmountTooLargeCode, $"The amount of line {lineNo} is larger than a bill line can hold.");

    /// <summary>Posting a draft that has no line: there is nothing to bill (docs/domain-model.md, section 4.4).</summary>
    public static Error BillHasNoLines() => Error.BusinessRule(BillHasNoLinesCode, "A bill needs at least one line to be posted.");
    /// <summary>
    /// Editing, voiding or posting a bill that is posted already (BR-006, BR-209): it has its number and its ledger
    /// entry, and is corrected only by a credit note or an adjustment. A conflict, because the usual cause is a
    /// second request for a bill somebody has just posted.
    /// </summary>
    public static Error BillAlreadyPosted()
        => Error.Conflict(BillAlreadyPostedCode, "The bill is posted already and can no longer be changed (BR-006). Correct it with a credit note or an adjustment.");
    /// <summary>
    /// Editing, voiding or posting a bill that waits for a second person (BR-207): it is locked until the checker
    /// approves it, which posts it, or rejects it, which makes it a draft again.
    /// </summary>
    public static Error BillPendingApproval()
        => Error.Conflict(BillPendingApprovalCode, "The bill is waiting for approval and cannot be changed until it is approved or rejected (BR-207).");

    /// <summary>Approving or rejecting a bill that is a draft: nobody has asked for an approval.</summary>
    public static Error BillNotPendingApproval()
        => Error.BusinessRule(BillNotPendingApprovalCode, "The bill is not waiting for approval, so there is nothing to approve or reject.");
}
