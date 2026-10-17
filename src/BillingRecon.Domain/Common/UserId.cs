namespace BillingRecon.Domain.Common;

/// <summary>Identifies a user. A user belongs to exactly one tenant (BR-601).</summary>
public readonly record struct UserId(Guid Value) : ITypedId
{
    /// <summary>The id as text, in the canonical 8-4-4-4-12 form.</summary>
    public override string ToString() => Value.ToString();
}
