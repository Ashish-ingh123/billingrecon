namespace BillingRecon.Domain.Adjustments;

/// <summary>
/// Where an adjustment is in its life (docs/domain-model.md, section 4.8). The member names are stored as text, so
/// renaming one is a data migration.
/// </summary>
public enum AdjustmentStatus
{
    /// <summary>Being written: it can be corrected and voided, and has no number and no ledger effect.</summary>
    Draft,

    /// <summary>
    /// Waiting for a second person (BR-502, BR-604): every adjustment does, a rounding of a few paise included. It is
    /// locked, has no number and no ledger effect; an approval posts it, a rejection makes it a draft again.
    /// </summary>
    PendingApproval,

    /// <summary>Approved and posted: it has its number and its ledger entry, and is frozen (BR-006).</summary>
    Posted,

    /// <summary>A draft that was abandoned. It keeps its content and can no longer change.</summary>
    Voided,
}
