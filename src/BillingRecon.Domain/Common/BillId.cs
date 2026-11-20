namespace BillingRecon.Domain.Common;

/// <summary>
/// Identifies a bill. It lives in Common, like the other ids modules share: payments, credit notes, adjustments and
/// the receivables ledger refer to a bill, and modules share ids, never aggregates (ADR 0003).
/// </summary>
public readonly record struct BillId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
