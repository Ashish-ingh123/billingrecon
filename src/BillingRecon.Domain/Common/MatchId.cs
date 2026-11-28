namespace BillingRecon.Domain.Common;

/// <summary>The id of a match of R2, which joins statement lines to what they are (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
