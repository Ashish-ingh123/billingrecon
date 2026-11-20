namespace BillingRecon.Domain.Common;

/// <summary>
/// Identifies one line of a bill. It lives in Common because a credit note line may name the bill line it credits
/// (BR-211), and modules share ids, never aggregates (ADR 0003).
/// </summary>
public readonly record struct BillLineId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
