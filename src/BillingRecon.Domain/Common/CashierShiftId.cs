namespace BillingRecon.Domain.Common;

/// <summary>The id of a cashier's shift. In Common, like every typed id: a ledger posting names the shift it belongs to.</summary>
public readonly record struct CashierShiftId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
