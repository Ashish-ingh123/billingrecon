using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>The failures of the Receivables module. The codes are part of the API contract (docs/api.md).</summary>
public static class ReceivablesErrors
{
    public const string LedgerBalanceNegativeCode = "ledger_balance_negative";

    /// <summary>
    /// A posting would take the receivable of a bill, the unapplied cash of a payment or the available credit of a
    /// credit note below zero (BR-405). The document that asked for it checks its own cap first, so reaching this is
    /// either a race the document lost or a defect; nothing is written.
    /// </summary>
    public static Error LedgerBalanceNegative()
        => Error.BusinessRule(LedgerBalanceNegativeCode, "The posting would take a balance below zero (BR-405). Read the document again and retry.");
}
