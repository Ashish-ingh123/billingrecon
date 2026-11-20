namespace BillingRecon.Domain.Billing;

/// <summary>
/// Where a bill is in its life (docs/domain-model.md, section 5.1). Members are added at the end, so their order is the
/// order in which the slices arrived, not the order of the life of a bill. The member names are stored as text (docs/database.md, section 3.3), so renaming one is a
/// data migration.
/// </summary>
public enum BillStatus
{
    /// <summary>The bill is being written: it can be edited and voided, and has no number and no ledger effect.</summary>
    Draft,

    /// <summary>A draft that was abandoned (BR-204). It keeps its content and can no longer change.</summary>
    Voided,

    /// <summary>Posted and wholly unpaid: it has its number and its ledger entry, and the balance due is the total (BR-206, BR-208).</summary>
    Posted,

    /// <summary>Posted, with a balance due above zero and below the total (BR-208). No use case reaches it yet.</summary>
    PartiallyPaid,

    /// <summary>Posted, with nothing left to pay (BR-208). A bill that comes to nothing is settled the moment it is posted (BR-206).</summary>
    Settled,

    /// <summary>
    /// Waiting for a second person, because the total is at or above the threshold of the tenant (BR-207). The bill
    /// is locked, has no number and no ledger effect; an approval posts it, a rejection makes it a draft again.
    /// </summary>
    PendingApproval,
}
