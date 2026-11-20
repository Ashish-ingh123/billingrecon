namespace BillingRecon.Domain.Billing;

/// <summary>
/// Where a bill is in its life (docs/domain-model.md, section 5.1). Only the two states a draft can be in exist so
/// far; the posting slices add PendingApproval, Posted, PartiallyPaid and Settled, each with a migration that widens
/// the CHECK constraint. The member names are stored as text (docs/database.md, section 3.3), so renaming one is a
/// data migration.
/// </summary>
public enum BillStatus
{
    /// <summary>The bill is being written: it can be edited and voided, and has no number and no ledger effect.</summary>
    Draft,

    /// <summary>A draft that was abandoned (BR-204). It keeps its content and can no longer change.</summary>
    Voided,
}
