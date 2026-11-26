namespace BillingRecon.Domain.Common;

/// <summary>The id of a stored image of a tenant's letterhead (ADR 0059).</summary>
public readonly record struct TenantImageId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
