namespace BillingRecon.Domain.Reconciliation;

/// <summary>Where a proposal of a matching run stands (BR-1011). Stored as text.</summary>
public enum MatchProposalStatus
{
    /// <summary>Waiting for a user to accept or dismiss it.</summary>
    Open,

    /// <summary>A user accepted it: it became a match.</summary>
    Accepted,

    /// <summary>A user dismissed it.</summary>
    Dismissed,

    /// <summary>A later run, or a match made otherwise, took its place.</summary>
    Withdrawn,
}
