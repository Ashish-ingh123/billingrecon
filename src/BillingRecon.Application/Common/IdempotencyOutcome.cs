namespace BillingRecon.Application.Common;

/// <summary>What became of an attempt to reserve an idempotency key (BR-803, ADR 0006).</summary>
public enum IdempotencyOutcome
{
    /// <summary>The key was free and is now reserved for this request, which goes ahead.</summary>
    Reserved,

    /// <summary>The same request was answered before: the stored answer is returned and nothing runs.</summary>
    Replay,

    /// <summary>The same request is still running, or died while it was: 409 <c>idempotency_in_progress</c>.</summary>
    InProgress,

    /// <summary>The key belongs to another request: 422 <c>idempotency_key_reuse</c>.</summary>
    Reuse,
}
