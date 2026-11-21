namespace BillingRecon.Domain.Billing;

/// <summary>
/// Where a credit note is in its life (docs/domain-model.md, section 4.5). The member names are stored as text
/// (docs/database.md, section 3.3), so renaming one is a data migration.
/// </summary>
public enum CreditNoteStatus
{
    /// <summary>Being written: it can be edited and voided, and has no number and no ledger effect.</summary>
    Draft,

    /// <summary>
    /// Waiting for a second person (BR-212, BR-604): every credit note does before it is posted. It is locked, has no
    /// number and no ledger effect; an approval posts it, a rejection makes it a draft again.
    /// </summary>
    PendingApproval,

    /// <summary>Approved and posted: it has its number and its ledger entry, and is frozen (BR-006).</summary>
    Posted,

    /// <summary>A draft that was abandoned. It keeps its content and can no longer change.</summary>
    Voided,
}
