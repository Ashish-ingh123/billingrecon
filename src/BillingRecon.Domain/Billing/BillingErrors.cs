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
    public const string BillNotPostedCode = "bill_not_posted";
    public const string AllocationExceedsBalanceDueCode = "allocation_exceeds_balance_due";
    public const string AllocationPayerMismatchCode = "allocation_payer_mismatch";
    public const string CreditNoteNotFoundCode = "credit_note_not_found";
    public const string CreditNoteNotDraftCode = "credit_note_not_draft";
    public const string CreditNotePendingApprovalCode = "credit_note_pending_approval";
    public const string CreditNoteNotPendingApprovalCode = "credit_note_not_pending_approval";
    public const string CreditNoteAlreadyPostedCode = "credit_note_already_posted";
    public const string CreditNoteNotPostedCode = "credit_note_not_posted";
    public const string CreditNoteHasNoLinesCode = "credit_note_has_no_lines";
    public const string CreditNoteTotalZeroCode = "credit_note_total_zero";
    public const string CreditNoteDateInFutureCode = "credit_note_date_in_future";
    public const string CreditNoteDateTooOldCode = "credit_note_date_too_old";
    public const string CreditNoteDateBeforeBillDateCode = "credit_note_date_before_bill_date";
    public const string CreditNoteLineDiscountExceedsAmountCode = "credit_note_line_discount_exceeds_amount";
    public const string CreditNoteLineAmountTooLargeCode = "credit_note_line_amount_too_large";
    public const string CreditNoteBillLineNotFoundCode = "credit_note_bill_line_not_found";
    public const string CreditNoteExceedsBillTotalCode = "credit_note_exceeds_bill_total";
    public const string CreditNoteLineExceedsBillLineCode = "credit_note_line_exceeds_bill_line";
    public const string CreditExceedsAvailableCode = "credit_exceeds_available";
    public const string CreditApplicationNotFoundCode = "credit_application_not_found";
    public const string CreditApplicationMadeAtPostingCode = "credit_application_made_at_posting";
    public const string CreditApplicationPendingReversalCode = "credit_application_pending_reversal";
    public const string CreditApplicationReversedCode = "credit_application_reversed";
    public const string CreditApplicationNotPendingReversalCode = "credit_application_not_pending_reversal";

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

    /// <summary>An amount is to be applied to a bill that is a draft, voided, or waiting for approval: nothing is owed on it (BR-303).</summary>
    public static Error BillNotPosted()
        => Error.BusinessRule(BillNotPostedCode, "The bill is not posted, so nothing is owed on it and nothing can be applied to it (BR-303).");

    /// <summary>More is to be applied to a bill than is still owed on it (BR-303).</summary>
    public static Error AllocationExceedsBalanceDue(string number, Money balanceDue)
        => Error.BusinessRule(AllocationExceedsBalanceDueCode, $"Bill {number} has a balance due of {balanceDue}; more than that cannot be applied to it (BR-303).");

    /// <summary>The bill is owed by another payer than the one the money came from (BR-303).</summary>
    public static Error AllocationPayerMismatch()
        => Error.BusinessRule(AllocationPayerMismatchCode, "The bill is owed by a different payer than the payment came from (BR-303).");

    /// <summary>No credit note with that id exists for the tenant. Another tenant's credit note gets the same answer.</summary>
    public static Error CreditNoteNotFound() => Error.NotFound(CreditNoteNotFoundCode, "The credit note does not exist.");

    /// <summary>Editing, voiding or submitting a draft credit note that was voided.</summary>
    public static Error CreditNoteNotDraft()
        => Error.BusinessRule(CreditNoteNotDraftCode, "Only a draft credit note can be edited, voided or submitted.");

    /// <summary>Editing, voiding or submitting a credit note that waits for a second person (BR-212).</summary>
    public static Error CreditNotePendingApproval()
        => Error.Conflict(CreditNotePendingApprovalCode, "The credit note is waiting for approval and cannot be changed until it is approved or rejected (BR-212).");

    /// <summary>Approving or rejecting a credit note that is a draft: nobody has asked for an approval.</summary>
    public static Error CreditNoteNotPendingApproval()
        => Error.BusinessRule(CreditNoteNotPendingApprovalCode, "The credit note is not waiting for approval, so there is nothing to approve or reject.");

    /// <summary>Editing, voiding, submitting or deciding a credit note that is posted already (BR-006).</summary>
    public static Error CreditNoteAlreadyPosted()
        => Error.Conflict(CreditNoteAlreadyPostedCode, "The credit note is posted already and can no longer be changed (BR-006).");

    /// <summary>Applying credit from a credit note that is not posted: it has no credit yet (BR-213).</summary>
    public static Error CreditNoteNotPosted()
        => Error.BusinessRule(CreditNoteNotPostedCode, "The credit note is not posted, so it has no credit to apply (BR-213).");

    /// <summary>Submitting a draft credit note that has no line.</summary>
    public static Error CreditNoteHasNoLines()
        => Error.BusinessRule(CreditNoteHasNoLinesCode, "A credit note needs at least one line to be submitted.");

    /// <summary>Submitting a credit note that comes to nothing: there is nothing to credit.</summary>
    public static Error CreditNoteTotalZero()
        => Error.BusinessRule(CreditNoteTotalZeroCode, "A credit note needs a total above zero to be submitted.");

    /// <summary>The credit note date is after today in India (BR-205).</summary>
    public static Error CreditNoteDateInFuture()
        => Error.BusinessRule(CreditNoteDateInFutureCode, "The credit note date cannot be in the future (BR-205).");

    /// <summary>The credit note date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error CreditNoteDateTooOld(DateOnly earliest)
        => Error.BusinessRule(CreditNoteDateTooOldCode, $"The credit note date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");

    /// <summary>The credit note date is before the date of the bill it is against (BR-214).</summary>
    public static Error CreditNoteDateBeforeBillDate(DateOnly billDate)
        => Error.BusinessRule(CreditNoteDateBeforeBillDateCode, $"The credit note date cannot be before the bill date, {billDate:yyyy-MM-dd} (BR-214).");

    /// <summary>The discount of line <paramref name="lineNo"/> of a credit note is more than its amount (BR-211).</summary>
    public static Error CreditNoteLineDiscountExceedsAmount(int lineNo)
        => Error.BusinessRule(CreditNoteLineDiscountExceedsAmountCode, $"The discount of line {lineNo} is more than the amount of the line (BR-211).");

    /// <summary>The quantity times the unit price of line <paramref name="lineNo"/> of a credit note is more than a line can hold.</summary>
    public static Error CreditNoteLineAmountTooLarge(int lineNo)
        => Error.BusinessRule(CreditNoteLineAmountTooLargeCode, $"The amount of line {lineNo} is larger than a credit note line can hold.");

    /// <summary>Line <paramref name="lineNo"/> of a credit note names a line the bill does not have (BR-211).</summary>
    public static Error CreditNoteBillLineNotFound(int lineNo)
        => Error.BusinessRule(CreditNoteBillLineNotFoundCode, $"Line {lineNo} names a line the bill does not have (BR-211).");

    /// <summary>
    /// The posted credit notes of a bill, with this one, would come to more than the bill, or, since 2026-11-26, than
    /// what was billed and not taken off by adjustments (BR-210; ADR 0025, amended): one failure, one code.
    /// </summary>
    public static Error CreditNoteExceedsBillTotal(Money billTotal, Money alreadyCredited, Money alreadyAdjusted = default)
        => alreadyAdjusted == Money.Zero
            ? Error.BusinessRule(CreditNoteExceedsBillTotalCode, $"The bill comes to {billTotal} and {alreadyCredited} of it is credited already; this credit note would take the credits above the bill (BR-210).")
            : Error.BusinessRule(CreditNoteExceedsBillTotalCode, $"The bill comes to {billTotal}; {alreadyCredited} of it is credited already and {alreadyAdjusted} was taken off by adjustments. This credit note would credit more than is left of the bill (BR-210).");

    /// <summary>The credits for one line of the bill, with line <paramref name="lineNo"/>, would come to more than that line (BR-211).</summary>
    public static Error CreditNoteLineExceedsBillLine(int lineNo, Money billLineTotal, Money alreadyCredited)
        => Error.BusinessRule(CreditNoteLineExceedsBillLineCode, $"Line {lineNo} credits a bill line of {billLineTotal}, of which {alreadyCredited} is credited already; that is more than the line (BR-211).");

    /// <summary>More is to be applied than the credit note has available (BR-213).</summary>
    public static Error CreditExceedsAvailable(string number, Money available)
        => Error.BusinessRule(CreditExceedsAvailableCode, $"Credit note {number} has {available} available; more than that cannot be applied (BR-213).");

    /// <summary>The credit note has no application with that id.</summary>
    public static Error CreditApplicationNotFound() => Error.NotFound(CreditApplicationNotFoundCode, "The credit note has no such application.");

    /// <summary>
    /// Reversing the application a credit note made to its own bill when it was posted (ADR 0028): that application is
    /// what the credit note corrects, and it is never reversed.
    /// </summary>
    public static Error CreditApplicationMadeAtPosting()
        => Error.BusinessRule(CreditApplicationMadeAtPostingCode, "This credit was applied to its own bill when the credit note was posted, and that application cannot be reversed (BR-212).");

    /// <summary>Asking to reverse an application that already waits for that decision: one decision at a time.</summary>
    public static Error CreditApplicationPendingReversal()
        => Error.Conflict(CreditApplicationPendingReversalCode, "The application is waiting for a decision on its reversal already.");

    /// <summary>Reversing an application that was reversed already.</summary>
    public static Error CreditApplicationReversed()
        => Error.Conflict(CreditApplicationReversedCode, "The application was reversed already.");

    /// <summary>Approving or rejecting the reversal of an application nobody asked to reverse.</summary>
    public static Error CreditApplicationNotPendingReversal()
        => Error.BusinessRule(CreditApplicationNotPendingReversalCode, "The application is not waiting for a reversal, so there is nothing to approve or reject.");
}
