using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What a refusal of <see cref="IReceivablesLedger"/> means, for the modules that post through it and may not name
/// the Receivables domain themselves (ADR 0003).
/// </summary>
public static class LedgerRefusal
{
    /// <summary>
    /// Whether the ledger refused because a balance would go below zero (BR-405). For a use case whose documents
    /// accepted the amount as they were read, that means another request changed them in between.
    /// </summary>
    public static bool IsBalanceBelowZero(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code == ReceivablesErrors.LedgerBalanceNegativeCode;
    }
}
