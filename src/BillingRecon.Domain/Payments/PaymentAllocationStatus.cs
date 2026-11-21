namespace BillingRecon.Domain.Payments;

/// <summary>
/// Where an allocation is in its life (docs/domain-model.md, section 5.3). Members are added at the end; their names
/// are stored as text, so renaming one is a data migration.
/// </summary>
public enum PaymentAllocationStatus
{
    /// <summary>In force: the amount is applied to the bill.</summary>
    Active = 0,

    /// <summary>
    /// A cashier asked for the allocation to be reversed and a second person has not decided yet (BR-305). The amount
    /// is still applied to the bill; an approval reverses it, a rejection makes it <see cref="Active"/> again.
    /// </summary>
    PendingReversal = 1,

    /// <summary>Reversed (BR-305): the amount is back with the payment and the bill is owed it again. The row stays.</summary>
    Reversed = 2,
}
