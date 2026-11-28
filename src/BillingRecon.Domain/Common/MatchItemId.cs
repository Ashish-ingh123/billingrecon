namespace BillingRecon.Domain.Common;

/// <summary>The id of a payment or refund inside a match (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchItemId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
