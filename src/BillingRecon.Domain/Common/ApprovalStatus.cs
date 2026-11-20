namespace BillingRecon.Domain.Common;

/// <summary>Where an approval request stands (docs/domain-model.md, section 5.4). A decision is final.</summary>
public enum ApprovalStatus
{
    /// <summary>Waiting for a checker.</summary>
    Pending = 0,

    /// <summary>Approved by a checker: the action was carried out in the same transaction (BR-606).</summary>
    Approved = 1,

    /// <summary>Rejected by a checker, with a comment (BR-605).</summary>
    Rejected = 2,
}
