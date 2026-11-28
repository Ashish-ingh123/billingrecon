namespace BillingRecon.Domain.Common;

/// <summary>The id of a matching run of R2 (ADR 0066). In Common, like every typed id.</summary>
public readonly record struct MatchingRunId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
