namespace BillingRecon.Domain.Common;

/// <summary>The id of a refund. In Common, like every typed id.</summary>
public readonly record struct RefundId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
