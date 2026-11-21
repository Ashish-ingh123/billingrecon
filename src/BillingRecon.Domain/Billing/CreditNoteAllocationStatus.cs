namespace BillingRecon.Domain.Billing;

/// <summary>
/// Where the application of a credit note to a bill is in its life (docs/domain-model.md, section 4.5). Members are
/// added at the end; their names are stored as text, so renaming one is a data migration.
/// </summary>
public enum CreditNoteAllocationStatus
{
    /// <summary>In force: the amount is applied to the bill.</summary>
    Active = 0,

    /// <summary>
    /// A cashier asked for the application to be reversed and a second person has not decided yet (ADR 0028). The
    /// amount is still applied to the bill; an approval reverses it, a rejection makes it <see cref="Active"/> again.
    /// </summary>
    PendingReversal = 1,

    /// <summary>Reversed (ADR 0028): the amount is available credit again and the bill is owed it again. The row stays.</summary>
    Reversed = 2,
}
