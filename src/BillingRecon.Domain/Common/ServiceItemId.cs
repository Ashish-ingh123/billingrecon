namespace BillingRecon.Domain.Common;

/// <summary>
/// Identifies an entry of the service catalogue (docs/domain-model.md, section 4.3). It lives in Common, like the
/// other ids modules share: a bill line and a credit note line name the service item they were priced from, and
/// modules share ids, never aggregates (ADR 0003).
/// </summary>
public readonly record struct ServiceItemId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
