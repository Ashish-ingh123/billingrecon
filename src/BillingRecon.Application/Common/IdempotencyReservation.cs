namespace BillingRecon.Application.Common;

/// <summary>The answer of <see cref="IIdempotencyStore.ReserveAsync"/>: what happened, and the stored answer when it is a replay.</summary>
public sealed record IdempotencyReservation(IdempotencyOutcome Outcome, StoredResponse? Response = null)
{
    /// <summary>The key was free and is now reserved.</summary>
    public static IdempotencyReservation Reserved { get; } = new(IdempotencyOutcome.Reserved);

    /// <summary>The same request is still running.</summary>
    public static IdempotencyReservation InProgress { get; } = new(IdempotencyOutcome.InProgress);

    /// <summary>The key belongs to another request.</summary>
    public static IdempotencyReservation Reuse { get; } = new(IdempotencyOutcome.Reuse);

    /// <summary>The same request was answered with <paramref name="response"/>.</summary>
    public static IdempotencyReservation Replay(StoredResponse response) => new(IdempotencyOutcome.Replay, response);
}
