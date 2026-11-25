using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What the check of an opening batch's files found (ADR 0053): the opening receivables and advances that can be brought
/// in, in the order of their files, and every finding that stops the batch. With no finding the totals equal the trial
/// balance of the files.
/// </summary>
public sealed record OpeningBatchCheck(
    IReadOnlyList<OpeningReceivable> Receivables,
    IReadOnlyList<OpeningAdvance> Advances,
    IReadOnlyList<ImportFinding> Findings)
{
    /// <summary>The sum of the opening receivables.</summary>
    public Money ReceivableTotal => Receivables.Select(receivable => receivable.Outstanding).Sum();

    /// <summary>The sum of the opening advances, credit balances included.</summary>
    public Money AdvanceTotal => Advances.Select(advance => advance.Amount).Sum();

    /// <summary>The part of <see cref="AdvanceTotal"/> that was a credit balance in the system before.</summary>
    public Money CreditBalanceTotal => Advances.Where(advance => advance.CreditBalance).Select(advance => advance.Amount).Sum();
}
