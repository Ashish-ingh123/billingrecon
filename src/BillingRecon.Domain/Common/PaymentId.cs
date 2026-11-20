namespace BillingRecon.Domain.Common;

/// <summary>The id of a payment. In Common, like every typed id, so that other modules can refer to a payment.</summary>
public readonly record struct PaymentId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
