using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>The failures of the Receivables module. The codes are part of the API contract (docs/api.md).</summary>
public static class ReceivablesErrors
{
    public const string LedgerBalanceNegativeCode = "ledger_balance_negative";

    /// <summary>Code of <see cref="OpeningBatchNotFound"/>.</summary>
    public const string OpeningBatchNotFoundCode = "opening_batch_not_found";

    /// <summary>Code of <see cref="OpeningBatchNotPending"/>.</summary>
    public const string OpeningBatchNotPendingCode = "opening_batch_not_pending";

    /// <summary>Code of <see cref="OpeningBatchDuplicate"/>.</summary>
    public const string OpeningBatchDuplicateCode = "opening_batch_duplicate";

    /// <summary>Code of <see cref="OpeningBatchHasRejections"/>.</summary>
    public const string OpeningBatchHasRejectionsCode = "opening_batch_has_rejections";

    /// <summary>
    /// A posting would take the receivable of a bill, the unapplied cash of a payment or the available credit of a
    /// credit note below zero (BR-405). The document that asked for it checks its own cap first, so reaching this is
    /// either a race the document lost or a defect; nothing is written.
    /// </summary>
    public static Error LedgerBalanceNegative()
        => Error.BusinessRule(LedgerBalanceNegativeCode, "The posting would take a balance below zero (BR-405). Read the document again and retry.");

    /// <summary>No opening batch with that id exists for the tenant. Another tenant's batch gets the same answer.</summary>
    public static Error OpeningBatchNotFound() => Error.NotFound(OpeningBatchNotFoundCode, "The opening batch does not exist.");

    /// <summary>Deciding an opening batch that was approved or rejected already (ADR 0053).</summary>
    public static Error OpeningBatchNotPending()
        => Error.Conflict(OpeningBatchNotPendingCode, "The opening batch was decided already; it is approved or rejected once.");

    /// <summary>
    /// The same files for the same cut-over date were submitted before, and that batch waits or was approved
    /// (ADR 0053): opening balances are brought in once.
    /// </summary>
    public static Error OpeningBatchDuplicate()
        => Error.Conflict(OpeningBatchDuplicateCode, "These files were submitted before for this cut-over date; opening balances are brought in once.");

    /// <summary>
    /// The files of an opening batch hold rows that cannot be brought in, or their totals are not those of the trial
    /// balance (ADR 0053). <paramref name="rejected"/> rows are named, with their file, line and reason, by the dry run;
    /// nothing is written until there is none.
    /// </summary>
    public static Error OpeningBatchHasRejections(int rejected)
        => Error.BusinessRule(
            OpeningBatchHasRejectionsCode,
            $"The files cannot be brought in as they are: {rejected} finding(s). Run the dry run, which names each with its file, line and reason; nothing was written.");
}
