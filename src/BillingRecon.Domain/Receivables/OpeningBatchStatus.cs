namespace BillingRecon.Domain.Receivables;

/// <summary>Where an opening batch stands (ADR 0053). The member names are stored as text.</summary>
public enum OpeningBatchStatus
{
    /// <summary>Submitted by its maker and waiting for a second person; nothing is posted.</summary>
    PendingApproval = 0,

    /// <summary>Approved: every opening receivable and advance of the batch is posted.</summary>
    Approved = 1,

    /// <summary>Rejected: nothing was posted and nothing will be.</summary>
    Rejected = 2,
}
