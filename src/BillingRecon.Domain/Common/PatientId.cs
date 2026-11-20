namespace BillingRecon.Domain.Common;

/// <summary>
/// Identifies a patient (BR-101). It lives in Common, not in Payers, because other modules refer to patients: a bill is
/// raised for one (BR-106), and the Domain of one business module may not reference another (ADR 0003). Modules share
/// ids, never aggregates.
/// </summary>
public readonly record struct PatientId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
