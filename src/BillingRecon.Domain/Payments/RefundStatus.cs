namespace BillingRecon.Domain.Payments;

/// <summary>Where a refund is in its life (docs/domain-model.md, section 5.2). The member names are stored as text.</summary>
public enum RefundStatus
{
    /// <summary>Written, not yet handed over; it can be corrected and voided.</summary>
    Draft = 0,

    /// <summary>Handed over to a second person (BR-604); locked until the decision.</summary>
    PendingApproval = 1,

    /// <summary>Approved, numbered and in the ledger: the money left (BR-307). Never reversed (BR-308).</summary>
    Posted = 2,

    /// <summary>A draft that was given up. It keeps its content and never changes again.</summary>
    Voided = 3,
}
