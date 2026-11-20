namespace BillingRecon.Domain.Common;

/// <summary>
/// Where the first request of an idempotency key stands (ADR 0006). The member names are stored as text
/// (docs/database.md, section 3.6), so renaming one is a data migration.
/// </summary>
public enum IdempotencyStatus
{
    /// <summary>The key is reserved and its request is still running, or its process died while it was.</summary>
    InProgress,

    /// <summary>The request has been answered, and the answer is stored for replays.</summary>
    Completed,
}
