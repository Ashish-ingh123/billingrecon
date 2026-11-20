namespace BillingRecon.Domain.Common;

/// <summary>Identifies the row of one number series: one tenant, one document type, one fiscal year (BR-801).</summary>
public readonly record struct NumberSeriesId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
