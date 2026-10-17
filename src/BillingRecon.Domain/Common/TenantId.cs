namespace BillingRecon.Domain.Common;

/// <summary>Identifies a tenant: one hospital or clinic, the unit of isolation (BR-002).</summary>
public readonly record struct TenantId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
