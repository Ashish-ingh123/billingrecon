namespace BillingRecon.Domain.Reconciliation;

/// <summary>Where a matching run stands (BR-1029). Stored as text.</summary>
public enum MatchingRunStatus
{
    /// <summary>Under way: no other run of the channel may start.</summary>
    Running,

    /// <summary>Done, with its counts.</summary>
    Finished,

    /// <summary>Left running for longer than a run takes (the process stopped): a new run took its place.</summary>
    Abandoned,
}
