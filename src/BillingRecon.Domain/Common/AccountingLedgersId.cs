namespace BillingRecon.Domain.Common;

/// <summary>Identifies a hospital's names for its accountant's ledgers.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct AccountingLedgersId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
