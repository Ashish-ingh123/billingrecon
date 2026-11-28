namespace BillingRecon.Domain.Reconciliation;

/// <summary>How a match was made (BR-1010, BR-1011). Stored as text.</summary>
public enum MatchSource
{
    /// <summary>The matching run confirmed it by itself: the certain case of BR-1010.</summary>
    Automatic,

    /// <summary>A user accepted a proposal of a run.</summary>
    ProposalAccepted,

    /// <summary>A user matched by hand (Prompt 13.3).</summary>
    Manual,
}
