using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The receivables subledger, as a business module sees it (ADR 0005; BR-401, BR-404; CLAUDE.md section 4, rules 3
/// and 4). A use case that posts a document calls it inside its own unit of work, after its validation and after it
/// has taken its document number: the balanced posting becomes part of that unit of work, and the cached balances of
/// the payer move in the same transaction. The commit of the use case makes the document, its number, the posting
/// and the balances permanent together; anything that fails before it takes all of them back (BR-007).
/// </summary>
public interface IReceivablesLedger
{
    /// <summary>
    /// Posts <paramref name="template"/>. Fails with <c>ledger_balance_negative</c>, and writes nothing, when the
    /// posting would take the balance of a bill, a payment or a credit note below zero (BR-405).
    /// </summary>
    /// <exception cref="ArgumentException">The template is not a valid posting: unbalanced, or its lines break BR-403.</exception>
    public Task<Result> PostAsync(PostingTemplate template, CancellationToken ct);
}
