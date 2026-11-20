namespace BillingRecon.Domain.Common;

/// <summary>The id of a ledger posting. In Common, like every typed id, so that a document can refer to its posting.</summary>
public readonly record struct LedgerPostingId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
