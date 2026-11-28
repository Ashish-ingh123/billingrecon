namespace BillingRecon.Domain.Common;

/// <summary>
/// The id of a line of a bank's statement (ADR 0065). In Common, like every typed id: the matches of R2 name the lines
/// they join.
/// </summary>
public readonly record struct StatementLineId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
