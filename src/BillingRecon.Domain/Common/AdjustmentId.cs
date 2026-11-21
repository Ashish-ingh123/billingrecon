namespace BillingRecon.Domain.Common;

/// <summary>The id of an adjustment. In Common, like every typed id.</summary>
public readonly record struct AdjustmentId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
