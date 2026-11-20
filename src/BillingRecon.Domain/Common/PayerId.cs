namespace BillingRecon.Domain.Common;

/// <summary>
/// Identifies a payer: who owes a bill and pays it (BR-106). It lives in Common, not in Payers, because bills,
/// payments, credit notes, adjustments, refunds and the receivables ledger all refer to a payer, and the Domain of one
/// business module may not reference another (ADR 0003). Modules share ids, never aggregates.
/// </summary>
public readonly record struct PayerId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
