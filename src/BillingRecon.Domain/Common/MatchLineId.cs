namespace BillingRecon.Domain.Common;

/// <summary>The id of a statement line inside a match (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchLineId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
