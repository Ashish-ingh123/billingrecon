namespace BillingRecon.Domain.Common;

/// <summary>The id of a proposal of a matching run, which a user accepts or dismisses (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchProposalId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
