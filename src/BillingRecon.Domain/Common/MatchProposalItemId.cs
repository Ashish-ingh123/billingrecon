namespace BillingRecon.Domain.Common;

/// <summary>The id of a payment or refund a proposal names (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchProposalItemId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
