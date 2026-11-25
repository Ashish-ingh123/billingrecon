namespace BillingRecon.Domain.Common;

/// <summary>The id of an opening batch (ADR 0053).</summary>
public readonly record struct OpeningBatchId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
