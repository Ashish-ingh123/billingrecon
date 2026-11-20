namespace BillingRecon.Domain.Common;

/// <summary>The id of the allocation of a payment to a bill. In Common, like every typed id.</summary>
public readonly record struct PaymentAllocationId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
